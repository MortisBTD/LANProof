param([switch]$Interop)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if(-not $env:JAVA_HOME){
        $jdk=Get-ChildItem -LiteralPath (Join-Path $env:USERPROFILE '.jdks') -Directory -ErrorAction SilentlyContinue | Where-Object {$_.Name -like 'jdk-17*'} | Select-Object -First 1
        if($jdk){$env:JAVA_HOME=$jdk.FullName}
    }
    if(-not $env:JAVA_HOME){throw 'Set JAVA_HOME to JDK 17'}
    if(-not $env:ANDROID_HOME){$env:ANDROID_HOME=Join-Path $env:LOCALAPPDATA 'Android\Sdk'}
    if(-not (Test-Path -LiteralPath (Join-Path $env:ANDROID_HOME 'platforms\android-35'))){throw 'Install Android SDK platform 35 and build-tools 35.0.0'}
    $env:Path=(Join-Path $env:JAVA_HOME 'bin')+';'+$env:Path
    $env:GRADLE_USER_HOME=Join-Path $projectRoot 'work\gradle'
    $env:ANDROID_USER_HOME=Join-Path $projectRoot 'work\android-user'
    $env:LANPROOF_ANDROID_QA_DIR=Join-Path $projectRoot 'work\android-preview'
    New-Item -ItemType Directory -Force $env:ANDROID_USER_HOME | Out-Null
    $peer=$null
    try {
        if($Interop){
            $build=Join-Path $projectRoot 'work\interop-harness'
            dotnet build tests/LANProof.CompanionHarness -c Release -o $build --nologo
            if($LASTEXITCODE -ne 0){throw 'Harness build failed'}
            $env:LANPROOF_INTEROP_DIR=Join-Path $projectRoot ('work\interop-'+[guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Force $env:LANPROOF_INTEROP_DIR | Out-Null
            $assembly=Join-Path $build 'LANProof.CompanionHarness.dll'
            $peer=Start-Process -FilePath 'dotnet' -ArgumentList @(('"{0}"' -f $assembly),('"{0}"' -f $env:LANPROOF_INTEROP_DIR)) -WindowStyle Hidden -PassThru
            $ready=Join-Path $env:LANPROOF_INTEROP_DIR 'server.json'
            for($attempt=0;$attempt -lt 60 -and -not (Test-Path -LiteralPath $ready);$attempt++){Start-Sleep -Milliseconds 250}
            if(-not (Test-Path -LiteralPath $ready)){throw 'Loopback peer did not start'}
        }else {Remove-Item Env:LANPROOF_INTEROP_DIR -ErrorAction SilentlyContinue}
        & .\android\gradlew.bat -p android :app:testDebugUnitTest :app:lintDebug :app:assembleDebug --console plain
        if($LASTEXITCODE -ne 0){throw 'Android verification failed'}
        $destination=Join-Path $projectRoot 'publish'
        New-Item -ItemType Directory -Force $destination | Out-Null
        Copy-Item -LiteralPath android/app/build/outputs/apk/debug/app-debug.apk -Destination (Join-Path $destination 'LANProof-Android-0.2.0-beta.1.apk') -Force
    }finally {if($peer -and -not $peer.HasExited){Stop-Process -Id $peer.Id}}
}finally {Pop-Location}
