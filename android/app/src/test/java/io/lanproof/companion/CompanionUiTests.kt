package io.lanproof.companion

import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.onRoot
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.performClick
import androidx.compose.ui.graphics.asAndroidBitmap
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

@RunWith(RobolectricTestRunner::class)
@Config(sdk=[34],qualifiers="w411dp-h891dp-xhdpi")
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class CompanionUiTests {
    @get:Rule val compose=createAndroidComposeRule<MainActivity>()
    @Test fun activityShowsRecordingAndPairingControls() {
        compose.onNodeWithText("LANProof").assertExists()
        compose.onNodeWithText("Начать запись").assertExists()
        compose.onNodeWithText("QR-код").performScrollTo().assertExists()
        compose.onNodeWithText("Подключить").assertExists()
    }
    @Test fun bothThemesRender() {
        val directory=System.getenv("LANPROOF_ANDROID_QA_DIR")
        var previousBackground:Int?=null
        for(theme in listOf("dark","light")) {
            compose.waitForIdle()
            lateinit var bitmap:android.graphics.Bitmap
            compose.runOnUiThread {
                val view=compose.activity.window.decorView
                bitmap=android.graphics.Bitmap.createBitmap(view.width,view.height,android.graphics.Bitmap.Config.ARGB_8888)
                view.draw(android.graphics.Canvas(bitmap))
            }
            org.junit.Assert.assertTrue(bitmap.width>100 && bitmap.height>100)
            val background=bitmap.getPixel(5,bitmap.height/2)
            previousBackground?.let{org.junit.Assert.assertNotEquals(it,background)};previousBackground=background
            if(!directory.isNullOrEmpty()) {
                val output=java.io.File(directory).apply{mkdirs()}
                java.io.File(output,"android-$theme.png").outputStream().use{bitmap.compress(android.graphics.Bitmap.CompressFormat.PNG,100,it)}
            }
            if(theme=="dark")compose.onNodeWithText("Светлая тема").performClick()
        }
    }
}
