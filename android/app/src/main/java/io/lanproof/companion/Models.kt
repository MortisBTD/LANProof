package io.lanproof.companion

import org.json.JSONArray
import org.json.JSONObject

data class Observation(val probeType:String,val target:String,val isSuccess:Boolean,val isConclusive:Boolean=true,
    val roundTripMs:Double?=null,val jitterMs:Double?=null,val packetLossPercent:Double?=null,
    val dnsResolveMs:Double?=null,val httpStatusCode:Int?=null,val details:String="") {
    fun json()=JSONObject().put("probeType",probeType).put("target",target).put("isSuccess",isSuccess).put("isConclusive",isConclusive)
        .put("roundTripMs",roundTripMs?:JSONObject.NULL).put("jitterMs",jitterMs?:JSONObject.NULL)
        .put("packetLossPercent",packetLossPercent?:JSONObject.NULL).put("dnsResolveMs",dnsResolveMs?:JSONObject.NULL)
        .put("httpStatusCode",httpStatusCode?:JSONObject.NULL).put("details",details.take(4096))
    companion object {
        fun fromJson(p:JSONObject)=Observation(p.getString("probeType"),p.getString("target"),p.getBoolean("isSuccess"),p.getBoolean("isConclusive"),
            p.number("roundTripMs"),p.number("jitterMs"),p.number("packetLossPercent"),p.number("dnsResolveMs"),
            if(p.isNull("httpStatusCode"))null else p.getInt("httpStatusCode"),p.optString("details"))
    }
}
fun JSONObject.number(key:String):Double?=if(isNull(key))null else getDouble(key)
data class Cycle(val sequence:Long=0,val timestampUtcMs:Long,val clockUncertaintyMs:Double?,val observations:List<Observation>,val elapsedRealtimeMs:Long?=null,val bootCount:Int?=null) {
    fun json()=JSONObject().put("sequence",sequence).put("timestampUtcMs",timestampUtcMs)
        .put("clockUncertaintyMs",clockUncertaintyMs?:JSONObject.NULL).put("observations",JSONArray(observations.map{it.json()}))
        .put("elapsedRealtimeMs",elapsedRealtimeMs?:JSONObject.NULL).put("bootCount",bootCount?:JSONObject.NULL)
    companion object {
        fun fromJson(p:JSONObject):Cycle {
            val list=p.getJSONArray("observations")
            return Cycle(p.getLong("sequence"),p.getLong("timestampUtcMs"),p.number("clockUncertaintyMs"),
                (0 until list.length()).map{Observation.fromJson(list.getJSONObject(it))},if(p.isNull("elapsedRealtimeMs"))null else p.getLong("elapsedRealtimeMs"),if(p.isNull("bootCount"))null else p.getInt("bootCount"))
        }
    }
}
data class ClockAnchor(val serverUtcMs:Long,val elapsedMs:Long,val uncertaintyMs:Double,val bootCount:Int) {
    fun align(cycle:Cycle):Cycle {
        val elapsed=cycle.elapsedRealtimeMs?:return cycle
        val age=elapsed-elapsedMs
        if(cycle.clockUncertaintyMs!=null || cycle.bootCount!=bootCount || kotlin.math.abs(age)>86_400_000)return cycle
        return cycle.copy(timestampUtcMs=serverUtcMs+age,clockUncertaintyMs=uncertaintyMs+kotlin.math.abs(age)*0.0001)
    }
    fun at(elapsedNow:Long,bootNow:Int):Pair<Long,Double?> {
        val age=elapsedNow-elapsedMs
        if(bootNow!=bootCount || age<0 || age>86_400_000) return System.currentTimeMillis() to null
        return serverUtcMs+age to uncertaintyMs+age*0.0001 // Conservative 100 ppm drift budget.
    }
}
