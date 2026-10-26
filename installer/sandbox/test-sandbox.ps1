# Runs inside Windows Sandbox: silent install, then reports progress until the game server runs.
$report = "C:\Test\sandbox\report"
New-Item -ItemType Directory -Force $report | Out-Null
Remove-Item "$report\*" -Force -ErrorAction SilentlyContinue
function Log($m) { "$(Get-Date -Format 'HH:mm:ss') $m" | Tee-Object -FilePath "$report\report.txt" -Append }

$setup = Get-ChildItem C:\Test\dist\EnshroudedManager-Setup-*.exe | Select-Object -First 1
$password = "Sandbox-Test-1"
Log "Installation de $($setup.Name)"
$p = Start-Process $setup.FullName -Wait -PassThru -ArgumentList @(
    "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LANG=fr", "/COMPONENTS=service,web,desktop", "/TASKS=desktopicon",
    "/LOG=$report\setup.log", "/SERVERDIR=C:\EnshroudedServer", "/PASSWORD=$password", "/SERVERNAME=Sandbox Test", "/SLOTS=4")
Log "Installeur terminé, code $($p.ExitCode)"
Log ("Service : " + (Get-Service EnshroudedManager -ErrorAction SilentlyContinue).Status)
Log ("Raccourcis : " + ((Get-ChildItem "$env:PUBLIC\Desktop", "$env:ProgramData\Microsoft\Windows\Start Menu\Programs" -Filter "Enshrouded*" | ForEach-Object Name) -join ", "))

$base = "http://localhost:8090/api"
Start-Sleep 5
try {
    Log ("auth/state : " + (Invoke-RestMethod "$base/auth/state" | ConvertTo-Json -Compress))
    $token = (Invoke-RestMethod -Method Post "$base/auth/login" -ContentType "application/json" -Body (@{ password = $password } | ConvertTo-Json)).token
    $h = @{ Authorization = "Bearer $token" }
    Log "Connexion OK"
} catch { Log "ERREUR API : $_"; exit }

$deadline = (Get-Date).AddMinutes(60)
while ((Get-Date) -lt $deadline) {
    $s = Invoke-RestMethod "$base/status" -Headers $h
    Log "état=$($s.state) build=$($s.localBuild) busy=$($s.busy) progress=$($s.progress)"
    if ($s.state -eq "Running") { break }
    Start-Sleep 30
}
Start-Sleep 60
$s = Invoke-RestMethod "$base/status" -Headers $h
Log "FINAL état=$($s.state) joueurs=$($s.players | ConvertTo-Json -Compress) build=$($s.localBuild)/$($s.remoteBuild)"
(Invoke-RestMethod "$base/logs" -Headers $h) | Where-Object { $_.message -notlike 'steamcmd : Update state*' } |
    ForEach-Object { "$($_.time) [$($_.level)] $($_.message)" } | Out-File "$report\manager-log.txt" -Encoding utf8
Copy-Item C:\EnshroudedServer\enshrouded_server.json "$report\" -ErrorAction SilentlyContinue
netsh advfirewall firewall show rule name=all | Select-String -Pattern "Enshrouded" -Context 0,2 | Out-File "$report\firewall.txt" -Encoding utf8
Log "TEST TERMINÉ"
