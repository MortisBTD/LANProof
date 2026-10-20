package io.lanproof.companion

import android.content.Context
import android.net.ConnectivityManager
import android.os.SystemClock
import kotlinx.coroutines.*
import java.net.Inet4Address
import java.net.URL
import java.util.concurrent.*
import javax.net.ssl.HttpsURLConnection

class WifiProbes(private val context:Context):AutoCloseable {
    private val dnsWorkers=ThreadPoolExecutor(2,2,30,TimeUnit.SECONDS,ArrayBlockingQueue(4),ThreadPoolExecutor.AbortPolicy())
    suspend fun measure():List<Observation> = coroutineScope {
        val network=CompanionClient.wifi(context)
        if(network==null) return@coroutineScope listOf(Observation("Adapter","Wi-Fi",false,details="Wi-Fi отсутствует. Мобильная сеть не используется."))+
            listOf("Gateway","External","Dns","Http").map{Observation(it,"Wi-Fi",false,false,details="Нет Wi-Fi; проверка не выполнялась")}
        val cm=context.getSystemService(ConnectivityManager::class.java)
        val link=cm.getLinkProperties(network);val iface=link?.interfaceName
        val gateway=link?.routes?.firstOrNull{it.isDefaultRoute && it.gateway is Inet4Address}?.gateway?.hostAddress
        val adapter=Observation("Adapter","Wi-Fi",true,details="Интерфейс ${iface?:"неизвестен"}; IPv4-шлюз ${gateway?:"не найден"}. Измерения привязаны к Wi-Fi.")
        val gw=async(Dispatchers.IO){if(gateway!=null && iface!=null) ping("Gateway",gateway,iface) else Observation("Gateway","Wi-Fi",false,false,details="IPv4-шлюз недоступен")}
        val external=async(Dispatchers.IO){
            if(iface==null) Observation("External","1.1.1.1",false,false,details="Не определён интерфейс Wi-Fi")
            else ping("External","1.1.1.1",iface)
        }
        val dns=async(Dispatchers.IO){
            var future:Future<Array<java.net.InetAddress>>?=null
            try {
                val start=SystemClock.elapsedRealtimeNanos();future=dnsWorkers.submit<Array<java.net.InetAddress>>{network.getAllByName("example.com")}
                val values=future.get(2000,TimeUnit.MILLISECONDS)
                Observation("Dns","example.com",values.isNotEmpty(),dnsResolveMs=(SystemClock.elapsedRealtimeNanos()-start)/1_000_000.0,details="Системный DNS выбранного Wi-Fi; кеш возможен")
            }catch(e:RejectedExecutionException){Observation("Dns","example.com",false,false,details="Резолвер занят предыдущими запросами")}
            catch(e:Exception){future?.cancel(true);Observation("Dns","example.com",false,details="DNS: ${e.javaClass.simpleName}")}
        }
        val http=async(Dispatchers.IO){
            var connection:HttpsURLConnection?=null
            try {
                val start=SystemClock.elapsedRealtimeNanos()
                connection=network.openConnection(URL("https://www.google.com/generate_204")) as HttpsURLConnection
                connection.connectTimeout=1800;connection.readTimeout=1800;connection.requestMethod="HEAD";connection.instanceFollowRedirects=false
                val status=connection.responseCode
                Observation("Http","https://www.google.com/generate_204",status in 200..299,roundTripMs=(SystemClock.elapsedRealtimeNanos()-start)/1_000_000.0,
                    httpStatusCode=status,details="Wi-Fi HTTPS HEAD до заголовков, включая сервис и холодное соединение; не ICMP RTT")
            }catch(e:Exception){Observation("Http","https://www.google.com/generate_204",false,details="HTTPS: ${e.javaClass.simpleName}")}
            finally{connection?.disconnect()}
        }
        listOf(adapter,gw.await(),external.await(),dns.await(),http.await())
    }
    private fun ping(type:String,target:String,iface:String):Observation {
        var process:Process?=null
        return try {
            process=ProcessBuilder("/system/bin/ping","-n","-c","2","-W","1","-w","3","-I",iface,target).redirectErrorStream(true).start()
            if(!process.waitFor(3500,TimeUnit.MILLISECONDS)){process.destroyForcibly();return Observation(type,target,false,false,details="ICMP-процесс превысил лимит")}
            parsePing(type,target,process.inputStream.bufferedReader().readText().take(4096),process.exitValue())
        }catch(e:Exception){Observation(type,target,false,false,details="ICMP недоступен на этом Android: ${e.javaClass.simpleName}")}
        finally {process?.destroy()}
    }
    override fun close(){dnsWorkers.shutdownNow()}
    companion object {
        fun parsePing(type:String,target:String,output:String,exit:Int):Observation {
            val counts=Regex("(\\d+) packets transmitted,\\s*(\\d+) (?:packets )?received").find(output)
            if(counts==null || exit !in 0..1) return Observation(type,target,false,false,details="ICMP не поддерживается или запрещён прошивкой: "+output.takeLast(200))
            val sent=counts.groupValues[1].toInt();val received=counts.groupValues[2].toInt()
            if(sent<=0 || received>sent)return Observation(type,target,false,false,details="Не удалось разобрать ICMP-статистику")
            val rtt=Regex("time[=<]([0-9.]+)").findAll(output).mapNotNull{it.groupValues[1].toDoubleOrNull()}.toList()
            val jitter=rtt.zipWithNext{a,b->kotlin.math.abs(a-b)}.takeIf{it.isNotEmpty()}?.average()
            return Observation(type,target,received>0,roundTripMs=rtt.takeIf{it.isNotEmpty()}?.average(),jitterMs=jitter,
                packetLossPercent=100.0*(sent-received)/sent,details="Wi-Fi ICMP: $received/$sent ответов; root не используется")
        }
    }
}
