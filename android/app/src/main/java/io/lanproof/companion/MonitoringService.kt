package io.lanproof.companion

import android.app.*
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.*
import android.provider.Settings
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

data class MonitorState(val running:Boolean=false,val latest:Cycle?=null,val queued:Long=0,val sync:String="Не подключено",val error:String?=null)
object CompanionState {val state=MutableStateFlow(MonitorState())}

class MonitoringService:Service() {
    private val scope=CoroutineScope(SupervisorJob()+Dispatchers.IO)
    private val syncGate=Mutex()
    private lateinit var store:MeasurementStore
    private lateinit var preferences:SecurePreferences
    private lateinit var probes:WifiProbes
    private var started=false
    private var wake:PowerManager.WakeLock?=null
    override fun onCreate(){super.onCreate();store=MeasurementStore(this);preferences=SecurePreferences(this);probes=WifiProbes(this)}
    override fun onBind(intent:Intent?)=null
    override fun onStartCommand(intent:Intent?,flags:Int,startId:Int):Int {
        if(intent?.action=="stop"){stopSelf();return START_NOT_STICKY}
        if(started)return START_NOT_STICKY
        started=true
        val manager=getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(NotificationChannel("monitor","Мониторинг Wi-Fi",NotificationManager.IMPORTANCE_LOW))
        val open=PendingIntent.getActivity(this,0,Intent(this,MainActivity::class.java),PendingIntent.FLAG_IMMUTABLE)
        val stop=PendingIntent.getService(this,1,Intent(this,MonitoringService::class.java).setAction("stop"),PendingIntent.FLAG_IMMUTABLE)
        val notification=Notification.Builder(this,"monitor").setSmallIcon(io.lanproof.companion.R.drawable.ic_lanproof)
            .setContentTitle("LANProof · запись Wi-Fi").setContentText("Данные сохраняются на телефоне даже без ПК")
            .setContentIntent(open).setOngoing(true).addAction(Notification.Action.Builder(null,"Остановить",stop).build()).build()
        if(Build.VERSION.SDK_INT>=34)startForeground(1,notification,ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE) else startForeground(1,notification)
        wake=getSystemService(PowerManager::class.java).newWakeLock(PowerManager.PARTIAL_WAKE_LOCK,"LANProof:Monitoring").apply{setReferenceCounted(false);acquire(60*60*1000L)}
        CompanionState.state.value=CompanionState.state.value.copy(running=true,error=null)
        scope.launch {
            try {
                var next=SystemClock.elapsedRealtime()
                while(isActive) {
                    if(wake?.isHeld==false)wake?.acquire(60*60*1000L)
                    val boot=Settings.Global.getInt(contentResolver,Settings.Global.BOOT_COUNT,0)
                    val stamp=preferences.anchor()?.at(SystemClock.elapsedRealtime(),boot)?:Pair(System.currentTimeMillis(),null)
                    val elapsed=SystemClock.elapsedRealtime()
                    val cycle=Cycle(timestampUtcMs=stamp.first,clockUncertaintyMs=stamp.second,observations=probes.measure(),elapsedRealtimeMs=elapsed,bootCount=boot)
                    val id=store.append(cycle)
                    CompanionState.state.value=CompanionState.state.value.copy(latest=cycle.copy(sequence=id),queued=store.queued())
                    next+=5000;delay((next-SystemClock.elapsedRealtime()).coerceAtLeast(0));if(next<SystemClock.elapsedRealtime()-5000)next=SystemClock.elapsedRealtime()
                }
            }catch(e:CancellationException){throw e}
            catch(e:Exception){CompanionState.state.value=CompanionState.state.value.copy(error=e.message?:"Не удалось сохранить измерение");stopSelf()}
        }
        scope.launch {while(isActive){syncGate.withLock{syncOnce(this@MonitoringService,preferences,store)};delay(5000)}}
        return START_NOT_STICKY
    }
    override fun onDestroy() {
        scope.cancel();probes.close();if(wake?.isHeld==true)wake?.release()
        CompanionState.state.value=CompanionState.state.value.copy(running=false)
        // Worker cancellation finishes before SQLiteOpenHelper is collected; do not close a DB under a running write.
        super.onDestroy()
    }
    companion object {
        suspend fun syncOnce(context:Context,prefs:SecurePreferences,store:MeasurementStore)=withContext(Dispatchers.IO) {
            try {
                val pairing=prefs.pairing()?:return@withContext
                if(!pairing.paired)return@withContext
                val client=CompanionClient(context,prefs)
                val hello=client.exchange(pairing,"hello")
                check(hello.getString("status")=="ok"){"ПК отозвал доступ: выполните сопряжение заново"}
                val expired=store.expireOldQueue(hello.getLong("serverUtcMs"),prefs.anchor())
                if(expired>0)CompanionState.state.value=CompanionState.state.value.copy(error="Удалено $expired циклов вне срока 29 дней или с невосстановимым временем; ПК их не принимает")
                repeat(3) {
                    val batch=store.pending().map{prefs.anchor()?.align(it)?:it};if(batch.isEmpty())return@repeat
                    val response=client.exchange(pairing,"sync",batch)
                    check(response.getString("status")=="ok" && response.getLong("acknowledgedThrough")==batch.maxOf{it.sequence}){"ПК не подтвердил пакет; очередь сохранена"}
                    store.acknowledge(response.getLong("acknowledgedThrough"))
                }
                CompanionState.state.value=CompanionState.state.value.copy(queued=store.queued(),sync="Синхронизировано с ПК")
            }catch(e:Exception) {
                if(e is CancellationException)throw e
                CompanionState.state.value=CompanionState.state.value.copy(queued=store.queued(),sync=e.message?.take(160)?:"ПК недоступен; очередь сохранена")
            }
        }
    }
}
