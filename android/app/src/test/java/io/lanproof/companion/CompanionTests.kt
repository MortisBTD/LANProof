package io.lanproof.companion

import androidx.test.core.app.ApplicationProvider
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import java.io.*

@RunWith(RobolectricTestRunner::class)
@Config(sdk=[34])
class CompanionTests {
    private fun cycle(time:Long=System.currentTimeMillis())=Cycle(timestampUtcMs=time,clockUncertaintyMs=20.0,
        observations=listOf("Gateway","External","Dns","Http","Adapter").map{Observation(it,"fixture",true)})
    @Test fun queueSurvivesReopeningAndOnlyAcknowledgedRowsLeaveQueue() {
        val context=ApplicationProvider.getApplicationContext<android.content.Context>();context.deleteDatabase("measurements.db")
        MeasurementStore(context).use{it.append(cycle());it.append(cycle());assertEquals(2L,it.queued())}
        MeasurementStore(context).use {
            assertEquals(2L,it.queued());val batch=it.pending();assertEquals(5,batch[0].observations.size)
            it.acknowledge(batch[0].sequence);assertEquals(1L,it.queued());assertEquals(batch[1].sequence,it.pending().single().sequence)
            assertEquals(2L,it.total())
        }
    }
    @Test fun queueDoesNotLoseUnacknowledgedCycleAfterRetry() {
        val context=ApplicationProvider.getApplicationContext<android.content.Context>();context.deleteDatabase("measurements.db")
        MeasurementStore(context).use{store->store.append(cycle());assertEquals(store.pending(),store.pending());assertEquals(1L,store.queued())}
    }
    @Test fun initialWrongWallClockIsRealignedBeforeQueueRetention() {
        val context=ApplicationProvider.getApplicationContext<android.content.Context>();context.deleteDatabase("measurements.db")
        val now=System.currentTimeMillis()
        MeasurementStore(context).use{store->
            store.append(cycle(1234).copy(clockUncertaintyMs=null,elapsedRealtimeMs=5000,bootCount=3))
            store.append(cycle(now-40*86_400_000L))
            assertEquals(1,store.expireOldQueue(now,ClockAnchor(now,10_000,10.0,3)))
            assertEquals(now-5000,store.pending().single().timestampUtcMs)
        }
    }
    @Test fun failedIcmpAndUnsupportedIcmpAreDifferent() {
        val failure=WifiProbes.parsePing("External","1.1.1.1","2 packets transmitted, 0 received, 100% packet loss",1)
        assertTrue(failure.isConclusive);assertFalse(failure.isSuccess);assertEquals(100.0,failure.packetLossPercent!!,0.01)
        val unsupported=WifiProbes.parsePing("External","1.1.1.1","ping: Operation not permitted",2)
        assertFalse(unsupported.isConclusive);assertNull(unsupported.packetLossPercent)
    }
    @Test fun jitterUsesConsecutiveRepliesOfSameTarget() {
        val result=WifiProbes.parsePing("Gateway","192.168.1.1","time=10.0 ms\ntime=14.0 ms\n2 packets transmitted, 2 received, 0% packet loss",0)
        assertEquals(12.0,result.roundTripMs!!,0.01);assertEquals(4.0,result.jitterMs!!,0.01)
    }
    @Test fun clockUsesMonotonicTimeAndInvalidatesOnRebootOrOldAnchor() {
        val anchor=ClockAnchor(1_000_000,10_000,10.0,3)
        val stamp=anchor.at(15_000,3);assertEquals(1_005_000,stamp.first);assertEquals(10.5,stamp.second!!,0.01)
        assertNull(anchor.at(15_000,4).second);assertNull(anchor.at(90_000_000,3).second)
        val beforePairing=cycle().copy(timestampUtcMs=1234,clockUncertaintyMs=null,elapsedRealtimeMs=5000,bootCount=3)
        val aligned=anchor.align(beforePairing);assertEquals(995000,aligned.timestampUtcMs);assertEquals(10.5,aligned.clockUncertaintyMs!!,0.01)
        assertNull(anchor.align(beforePairing.copy(bootCount=4)).clockUncertaintyMs)
    }
    @Test fun pairingRejectsInternetEndpointsAndMissingPins() {
        val pin="ab".repeat(32);val secret="cd".repeat(32)
        val code="lanproof://pair?v=1&host=192.168.1.5&port=13791&pin=$pin&secret=$secret&expires=2000000000000"
        assertEquals("192.168.1.5",PairingCodes.parse(code).host)
        assertThrows(IllegalArgumentException::class.java){PairingCodes.parse(code.replace("192.168.1.5","8.8.8.8"))}
        assertThrows(IllegalArgumentException::class.java){PairingCodes.parse(code.replace(pin,"bad"))}
    }
    @Test fun wireUsesBigEndianLengthAndRejectsOversizedFrames() {
        val stream=ByteArrayOutputStream();val value=cycle().json();CompanionClient.write(DataOutputStream(stream),value)
        val bytes=stream.toByteArray();assertEquals(0,bytes[0].toInt());val parsed=CompanionClient.read(DataInputStream(ByteArrayInputStream(bytes)))
        assertEquals(5,parsed.getJSONArray("observations").length())
        val oversized=ByteArrayOutputStream();DataOutputStream(oversized).writeInt(CompanionClient.MAX_FRAME+1)
        assertThrows(IllegalArgumentException::class.java){CompanionClient.read(DataInputStream(ByteArrayInputStream(oversized.toByteArray())))}
    }
    @Test fun wrongCertificateFingerprintIsRejected() {
        val manager=CompanionClient.trustManager("ab".repeat(32))
        assertThrows(java.security.cert.CertificateException::class.java){manager.checkServerTrusted(emptyArray(),"RSA")}
    }
}
