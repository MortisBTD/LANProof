package io.lanproof.companion

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.os.Build
import android.os.SystemClock
import android.provider.Settings
import org.json.JSONArray
import org.json.JSONObject
import java.io.DataInputStream
import java.io.DataOutputStream
import java.net.URI
import java.net.Inet4Address
import java.security.MessageDigest
import java.security.cert.X509Certificate
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSocket
import javax.net.ssl.X509TrustManager

object PairingCodes {
    fun parse(code:String,now:Long=System.currentTimeMillis()):Pairing {
        val uri=URI(code.trim());require(uri.scheme=="lanproof" && uri.host=="pair" && uri.fragment==null){"Нужен код LANProof с ПК"}
        val fields=uri.rawQuery.split("&").associate{val p=it.split("=",limit=2);require(p.size==2);p[0] to p[1]}
        val host=fields.getValue("host");val octets=host.split('.').map{it.toIntOrNull()}
        require(octets.size==4 && octets.all{it!=null && it in 0..255} && (octets[0]==10 || (octets[0]==172 && octets[1] in 16..31) || (octets[0]==192 && octets[1]==168))) {"Нужен локальный IPv4-адрес ПК"}
        val port=fields.getValue("port").toInt();require(port in 1024..65535)
        val pin=fields.getValue("pin");val secret=fields.getValue("secret");require(pin.matches(Regex("[a-fA-F0-9]{64}")) && secret.matches(Regex("[a-fA-F0-9]{64}")))
        require(fields["v"]=="1");val expires=fields.getValue("expires").toLong()
        // The phone's wall clock may be wrong; final expiry is checked by the PC.
        require(expires>0)
        return Pairing(host,port,pin.lowercase(),secret.lowercase(),false,expires)
    }
}
class CompanionClient(private val context:Context,private val preferences:SecurePreferences) {
    companion object {
        const val MAX_FRAME=256*1024
        @Suppress("DEPRECATION") // Enumerate unvalidated Wi-Fi too: cellular may be the active network during the outage being measured.
        fun wifi(context:Context):Network? {
            val cm=context.getSystemService(ConnectivityManager::class.java)
            return cm.allNetworks.firstOrNull {val c=cm.getNetworkCapabilities(it);c?.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)==true && !c.hasTransport(NetworkCapabilities.TRANSPORT_VPN)}
        }
        fun fingerprint(certificate:X509Certificate)=MessageDigest.getInstance("SHA-256").digest(certificate.encoded).joinToString(""){"%02x".format(it)}
        fun trustManager(pin:String)=object:X509TrustManager {
            override fun getAcceptedIssuers():Array<X509Certificate> = emptyArray()
            override fun checkClientTrusted(chain:Array<X509Certificate>,authType:String)=throw java.security.cert.CertificateException("Client certificates unsupported")
            override fun checkServerTrusted(chain:Array<X509Certificate>,authType:String) {
                if(chain.isEmpty() || !MessageDigest.isEqual(fingerprint(chain[0]).toByteArray(),pin.toByteArray()))throw java.security.cert.CertificateException("Отпечаток ПК изменился: выполните сопряжение заново")
                chain[0].checkValidity()
            }
        }
        fun read(input:DataInputStream):JSONObject {val size=input.readInt();require(size in 2..MAX_FRAME){"Недопустимый размер ответа"};val bytes=ByteArray(size);input.readFully(bytes);return JSONObject(String(bytes,Charsets.UTF_8))}
        fun write(output:DataOutputStream,value:JSONObject) {val bytes=value.toString().toByteArray(Charsets.UTF_8);require(bytes.size<=MAX_FRAME);output.writeInt(bytes.size);output.write(bytes);output.flush()}
        fun transport(plain:java.net.Socket,pairing:Pairing,request:JSONObject):JSONObject {
            val ssl=SSLContext.getInstance("TLS").apply{init(null,arrayOf(trustManager(pairing.pin)),null)}
            try {
                plain.connect(java.net.InetSocketAddress(pairing.host,pairing.port),3000);plain.soTimeout=5000
                (ssl.socketFactory.createSocket(plain,pairing.host,pairing.port,true) as SSLSocket).use {socket->
                    socket.enabledProtocols=socket.supportedProtocols.filter{it=="TLSv1.2"||it=="TLSv1.3"}.toTypedArray();socket.startHandshake()
                    write(DataOutputStream(socket.outputStream),request)
                    return read(DataInputStream(socket.inputStream))
                }
            }finally{plain.close()}
        }
    }
    fun exchange(pairing:Pairing,action:String,cycles:List<Cycle>?=null):JSONObject {
        val network=wifi(context)?:error("Нет Wi-Fi: очередь сохранена на телефоне")
        val plain=network.socketFactory.createSocket()
        val request=JSONObject().put("version",1).put("action",action).put("deviceId",preferences.deviceId)
                    .put("deviceName",Build.MODEL.take(64).ifBlank{"Android"}).put("credential",pairing.credential)
        if(cycles!=null)request.put("cycles",JSONArray(cycles.map{it.json()}))
        val before=SystemClock.elapsedRealtime()
        val response=transport(plain,pairing,request);val after=SystemClock.elapsedRealtime()
                if(response.optString("status") in listOf("ok","paired")) preferences.saveAnchor(ClockAnchor(response.getLong("serverUtcMs"),before+(after-before)/2,(after-before)/2.0+5,
                    Settings.Global.getInt(context.contentResolver,Settings.Global.BOOT_COUNT,0)))
                return response
    }
}
