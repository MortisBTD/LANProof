package io.lanproof.companion

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import org.json.JSONObject
import java.security.KeyStore
import java.util.UUID
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

data class Pairing(val host:String,val port:Int,val pin:String,val credential:String,val paired:Boolean=false,val expires:Long=0)

@android.annotation.SuppressLint("ApplySharedPref") // Credentials and time anchors must be durable before acknowledging success; called on IO workers.
class SecurePreferences(context:Context) {
    private val prefs=context.getSharedPreferences("companion",Context.MODE_PRIVATE)
    val deviceId:String=prefs.getString("deviceId",null)?:UUID.randomUUID().toString().also{prefs.edit().putString("deviceId",it).commit()}
    var dark:Boolean
        get()=prefs.getBoolean("dark",true)
        set(value){prefs.edit().putBoolean("dark",value).apply()}
    private fun key():SecretKey {
        val store=KeyStore.getInstance("AndroidKeyStore").apply{load(null)}
        return (store.getKey("LANProofPairing",null) as? SecretKey)?:KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore").apply {
            init(KeyGenParameterSpec.Builder("LANProofPairing",KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build())
        }.generateKey()
    }
    fun savePairing(pairing:Pairing?) {
        if(pairing==null){prefs.edit().remove("pairing").remove("clock").commit();return}
        val json=JSONObject().put("host",pairing.host).put("port",pairing.port).put("pin",pairing.pin).put("credential",pairing.credential)
            .put("paired",pairing.paired).put("expires",pairing.expires).toString().toByteArray()
        val cipher=Cipher.getInstance("AES/GCM/NoPadding").apply{init(Cipher.ENCRYPT_MODE,key())}
        val result=cipher.iv+cipher.doFinal(json)
        check(prefs.edit().putString("pairing",Base64.encodeToString(result,Base64.NO_WRAP)).commit())
    }
    fun pairing():Pairing? {
        val encoded=prefs.getString("pairing",null)?:return null
        val bytes=Base64.decode(encoded,Base64.NO_WRAP)
        val cipher=Cipher.getInstance("AES/GCM/NoPadding").apply{init(Cipher.DECRYPT_MODE,key(),GCMParameterSpec(128,bytes.copyOfRange(0,12)))}
        val p=JSONObject(String(cipher.doFinal(bytes.copyOfRange(12,bytes.size))))
        return Pairing(p.getString("host"),p.getInt("port"),p.getString("pin"),p.getString("credential"),p.getBoolean("paired"),p.optLong("expires"))
    }
    fun anchor():ClockAnchor?=prefs.getString("clock",null)?.let {val p=JSONObject(it);ClockAnchor(p.getLong("utc"),p.getLong("elapsed"),p.getDouble("uncertainty"),p.getInt("boot"))}
    fun saveAnchor(anchor:ClockAnchor) {prefs.edit().putString("clock",JSONObject().put("utc",anchor.serverUtcMs).put("elapsed",anchor.elapsedMs).put("uncertainty",anchor.uncertaintyMs).put("boot",anchor.bootCount).toString()).commit()}
}
