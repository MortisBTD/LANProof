package io.lanproof.companion

import android.Manifest
import android.content.Intent
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.lifecycleScope
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import kotlinx.coroutines.*
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

class MainActivity:ComponentActivity() {
    private var code by mutableStateOf("")
    private var message by mutableStateOf("")
    private var paired by mutableStateOf(false)
    private var busy by mutableStateOf(false)
    private lateinit var prefs:SecurePreferences
    private lateinit var store:MeasurementStore
    private val scanner=registerForActivityResult(ScanContract()){result->result.contents?.let{code=it;message="Код считан. Нажмите «Подключить»."}}
    private val notificationPermission=registerForActivityResult(ActivityResultContracts.RequestPermission()){startMonitor()}
    override fun onCreate(savedInstanceState:Bundle?) {
        super.onCreate(savedInstanceState);prefs=SecurePreferences(this);store=MeasurementStore(this)
        try{paired=prefs.pairing()?.paired==true}catch(e:Exception){message="Ключ подключения недоступен. Выполните сопряжение заново."}
        CompanionState.state.value=CompanionState.state.value.copy(latest=store.latest(),queued=store.queued())
        setContent {
            var dark by remember{mutableStateOf(prefs.dark)}
            val state by CompanionState.state.collectAsState()
            val scheme=if(dark)darkColorScheme(primary=Color(0xFF64B1FF),onPrimary=Color(0xFF102238),background=Color(0xFF101926),surface=Color(0xFF1B2738),onSurface=Color(0xFFEAF0FA))
                else lightColorScheme(primary=Color(0xFF087CF4),background=Color(0xFFE8EEF6),surface=Color.White)
            MaterialTheme(colorScheme=scheme) {
                Surface(Modifier.fillMaxSize(),color=scheme.background) {
                    Column(Modifier.statusBarsPadding().navigationBarsPadding().verticalScroll(rememberScrollState()).padding(20.dp),verticalArrangement=Arrangement.spacedBy(16.dp)) {
                        Row(horizontalArrangement=Arrangement.spacedBy(12.dp),verticalAlignment=androidx.compose.ui.Alignment.CenterVertically) {
                            androidx.compose.foundation.Image(androidx.compose.ui.res.painterResource(R.drawable.ic_lanproof),"Логотип LANProof",Modifier.size(42.dp))
                            Text("LANProof",fontSize=30.sp)
                        }
                        Text("ANDROID · КОМПАНЬОН",fontSize=11.sp,color=scheme.primary)
                        Row(horizontalArrangement=Arrangement.spacedBy(10.dp)) {
                            Button(onClick={if(state.running)stopMonitor() else if(Build.VERSION.SDK_INT>=33)notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS) else startMonitor()}) {
                                Text(if(state.running)"Остановить" else "Начать запись")
                            }
                            TextButton(onClick={dark=!dark;prefs.dark=dark}){Text(if(dark)"Светлая тема" else "Тёмная тема")}
                        }
                        InfoCard {
                            Text(if(state.running)"Идёт запись Wi-Fi" else "Запись остановлена",fontSize=20.sp)
                            Text("В очереди: ${state.queued} циклов",fontFamily=FontFamily.Monospace)
                            Text(state.sync,fontSize=13.sp)
                            Text("Мобильный интернет не используется. Пока ПК недоступен, измерения остаются на телефоне.",fontSize=12.sp)
                        }
                        state.error?.let{Text(it,color=scheme.error,fontSize=13.sp)}
                        InfoCard {
                            Text("Последний цикл",fontSize=20.sp)
                            if(state.latest==null)Text("Измерения появятся после запуска записи.",fontSize=13.sp)
                            state.latest?.let {cycle->
                                Text(SimpleDateFormat("dd.MM HH:mm:ss",Locale.getDefault()).format(Date(cycle.timestampUtcMs)),fontFamily=FontFamily.Monospace)
                                cycle.observations.forEach {observation->
                                    val label=when(observation.probeType){"Adapter"->"Устройство";"Gateway"->"Шлюз";"External"->"Интернет";"Dns"->"DNS";else->"HTTPS"}
                                    val value=if(!observation.isConclusive)"Нет результата" else if(!observation.isSuccess)"Нет ответа" else
                                        (observation.dnsResolveMs?:observation.roundTripMs)?.let{"%.1f мс".format(it)}?:"Доступно"
                                    Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.SpaceBetween){Text(label,fontSize=13.sp);Text(value,fontSize=13.sp,fontFamily=FontFamily.Monospace)}
                                    if(observation.probeType in listOf("Gateway","External") && observation.isConclusive)
                                        Text("Потери: ${observation.packetLossPercent?.let{"%.0f%%".format(it)}?:"—"} · Джиттер: ${observation.jitterMs?.let{"%.1f мс".format(it)}?:"—"}",fontSize=11.sp,fontFamily=FontFamily.Monospace)
                                }
                                Text(if(cycle.clockUncertaintyMs==null)"Часы ещё не согласованы с ПК" else "Погрешность времени: ±${cycle.clockUncertaintyMs.toInt()} мс",fontSize=11.sp)
                            }
                        }
                        InfoCard {
                            Text(if(paired)"ПК подключён" else "Подключение к Windows",fontSize=20.sp)
                            Text("На ПК откройте «Компаньон», включите приём и создайте код. После запроса подтвердите телефон на ПК.",fontSize=13.sp)
                            OutlinedTextField(value=code,onValueChange={code=it},label={Text("Код подключения с ПК")},modifier=Modifier.fillMaxWidth(),maxLines=4)
                            Row(horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                                OutlinedButton(enabled=!busy,onClick={scanner.launch(ScanOptions().setDesiredBarcodeFormats(ScanOptions.QR_CODE).setPrompt("Код LANProof на ПК").setBeepEnabled(false).setOrientationLocked(false))}){Text("QR-код")}
                                Button(enabled=!busy,onClick={connect()}){Text(if(busy)"Подключаем…" else "Подключить")}
                            }
                            if(paired)OutlinedButton(enabled=!busy,onClick={lifecycleScope.launch{busy=true;try{MonitoringService.syncOnce(this@MainActivity,prefs,store)}finally{busy=false}}}){Text("Синхронизировать сейчас")}
                            if(message.isNotEmpty())SelectionContainer{Text(message,fontSize=13.sp)}
                        }
                        Text("ICMP может ограничиваться прошивкой. HTTPS измеряет время до заголовков, а не сетевой RTT. Android может ограничивать запись в энергосберегающем режиме.",fontSize=11.sp)
                        Text("0.2.0-beta.1 · Данные остаются на устройствах",fontSize=11.sp,color=scheme.primary)
                    }
                }
            }
        }
    }
    private fun startMonitor(){startForegroundService(Intent(this,MonitoringService::class.java))}
    @android.annotation.SuppressLint("ImplicitSamInstance") // Context.stopService matches the service component, not Intent identity.
    private fun stopMonitor(){stopService(Intent(this,MonitoringService::class.java))}
    private fun connect() {
        lifecycleScope.launch {
            busy=true
            try {
                val candidate=PairingCodes.parse(code);val client=CompanionClient(this@MainActivity,prefs)
                message="Ожидаем подтверждения на Windows…"
                withTimeout(120_000) {
                    while(true) {
                        val response=withContext(Dispatchers.IO){client.exchange(candidate,"pair")}
                        when(response.getString("status")) {
                            "paired"->{withContext(Dispatchers.IO){prefs.savePairing(candidate.copy(credential=response.getString("token"),paired=true))};paired=true;message="Подключено. Сертификат ПК закреплён, ключ хранится в Android Keystore.";break}
                            "pending"->delay(1500)
                            "busy"->error("На ПК ожидается подтверждение другого телефона")
                            else->error("Код недействителен или истёк. Создайте новый код на ПК.")
                        }
                    }
                }
                MonitoringService.syncOnce(this@MainActivity,prefs,store)
            }catch(e:Exception){if(e is CancellationException && e !is TimeoutCancellationException)throw e;message=e.message?:"Подключение не удалось"}
            finally{busy=false}
        }
    }
}
@Composable private fun InfoCard(content:@Composable ColumnScope.()->Unit) {
    Card(Modifier.fillMaxWidth(),shape=androidx.compose.foundation.shape.RoundedCornerShape(10.dp),colors=CardDefaults.cardColors(containerColor=MaterialTheme.colorScheme.surface)) {
        Column(Modifier.padding(18.dp),verticalArrangement=Arrangement.spacedBy(12.dp),content=content)
    }
}
