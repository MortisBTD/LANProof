package io.lanproof.companion

import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.json.JSONObject
import org.json.JSONArray
import java.io.File
import java.net.Socket
import java.util.UUID

@RunWith(RobolectricTestRunner::class)
@Config(sdk=[34])
class DesktopInteropTests {
    @Test fun androidTransportPairsAndReplaysAnOfflineQueueToRealDotnetTlsServer() {
        val directory=System.getenv("LANPROOF_INTEROP_DIR")
        assumeTrue("Run the loopback .NET harness to enable this integration test",!directory.isNullOrEmpty())
        val config=JSONObject(File(directory!!,"server.json").readText())
        val candidate=Pairing(config.getString("host"),config.getInt("port"),config.getString("fingerprint"),config.getString("secret"))
        val deviceId=UUID.randomUUID().toString()
        fun request(action:String,credential:String)=JSONObject().put("version",1).put("action",action).put("deviceId",deviceId).put("deviceName","Robolectric Android").put("credential",credential)
        fun send(pairing:Pairing,request:JSONObject)=CompanionClient.transport(Socket(),pairing,request)
        assertEquals("unauthorized",send(candidate,request("hello",candidate.credential)).getString("status"))
        var response=send(candidate,request("pair",candidate.credential))
        assertEquals("pending",response.getString("status"))
        repeat(10){if(response.getString("status")=="pending"){Thread.sleep(200);response=send(candidate,request("pair",candidate.credential))}}
        assertEquals("paired",response.getString("status"))
        val paired=candidate.copy(credential=response.getString("token"),paired=true)
        val rows=listOf("Gateway","External","Dns","Http","Adapter").map{Observation(it,"fixture",true)}
        val cycles=listOf(Cycle(1,System.currentTimeMillis()-10_000,20.0,rows),Cycle(2,System.currentTimeMillis()-5000,20.0,rows))
        val packet=request("sync",paired.credential).put("cycles",JSONArray(cycles.map{it.json()}))
        repeat(2){val ack=send(paired,packet);assertEquals("ok",ack.getString("status"));assertEquals(2L,ack.getLong("acknowledgedThrough"))}
        val count=File(directory,"imported-count.txt");repeat(30){if(!count.isFile || count.readText()!="10")Thread.sleep(100)}
        assertEquals("10",count.readText())
    }
}
