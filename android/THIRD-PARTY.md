# Third-party components

LANProof code is MIT licensed. Runtime libraries retain their own licenses:

| Component | Maintainer | License |
| --- | --- | --- |
| AndroidX / Jetpack Compose | Android Open Source Project / Google | Apache-2.0 |
| Kotlin and kotlinx.coroutines | JetBrains and contributors | Apache-2.0 |
| ZXing Android Embedded | JourneyApps and contributors | Apache-2.0 |
| ZXing Core | ZXing authors | Apache-2.0 |
| Gradle Wrapper | Gradle and contributors | Apache-2.0 |

The Apache 2.0 license is included in the APK assets as `third-party-licenses.txt`. Build/test tools such as JUnit and Robolectric have their own licenses and are not bundled into the application runtime. Dependency versions are declared in `app/build.gradle.kts`.
