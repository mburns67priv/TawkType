param([string]$Out = "$env:TEMP\tawktype-seed-vocabulary.json")

$spellings = @(
  'TawkType','Jupitor Studio','GitHub','PostgreSQL','Kubernetes','WebAssembly','OAuth','gRPC','NuGet',
  'Velopack','sherpa-onnx','Parakeet','Whisper','ONNX','CUDA','Vulkan','WPF','xUnit','MSBuild',
  'PowerShell','SQLite','Anthropic','Claude','Windows','Explorer','Notepad','Chromium','Electron',
  'TypeScript','JavaScript','Node.js','Docker','Terraform','Ansible','Grafana','Prometheus','Redis',
  'RabbitMQ','Kafka','Elasticsearch','Aoife Kowalski','Niamh Nguyen','Siobhan Okonkwo',
  'Caoimhe Rasmussen','Padraig Villanueva','Oisin Papadopoulos','Roisin Fitzgerald','Cillian Szymanski'
)

$replacements = @(
  @{ From = 'see sharp';      To = 'C#' },
  @{ From = 'dot net';        To = '.NET' },
  @{ From = 'sequel';         To = 'SQL' },
  @{ From = 'no sequel';      To = 'NoSQL' },
  @{ From = 'post gres';      To = 'PostgreSQL' },
  @{ From = 'kuber netes';    To = 'Kubernetes' },
  @{ From = 'tawk type';      To = 'TawkType' },
  @{ From = 'jupitor';        To = 'Jupitor Studio' },
  @{ From = 'git hub';        To = 'GitHub' },
  @{ From = 'java script';    To = 'JavaScript' },
  @{ From = 'type script';    To = 'TypeScript' },
  @{ From = 'node jay ess';   To = 'Node.js' },
  @{ From = 'ay pee eye';     To = 'API' },
  @{ From = 'you eye';        To = 'UI' },
  @{ From = 'you ex';         To = 'UX' },
  @{ From = 'cee ess ess';    To = 'CSS' },
  @{ From = 'aitch tee em ell'; To = 'HTML' },
  @{ From = 'ess dee kay';    To = 'SDK' },
  @{ From = 'eye dee ee';     To = 'IDE' },
  @{ From = 'see eye';        To = 'CI' },
  @{ From = 'pee are';        To = 'pull request' },
  @{ From = 'em vee vee em';  To = 'MVVM' },
  @{ From = 'tee dee dee';    To = 'TDD' },
  @{ From = 'why ay em ell';  To = 'YAML' },
  @{ From = 'jason file';     To = 'JSON file' },
  @{ From = 'ex em ell';      To = 'XML' },
  @{ From = 'ess vee gee';    To = 'SVG' },
  @{ From = 'you tee eff eight'; To = 'UTF-8' },
  @{ From = 'regex';          To = 'regular expression' },
  @{ From = 'repo';           To = 'repository' },
  @{ From = 'ess ess aitch';  To = 'SSH' },
  @{ From = 'aitch tee tee pee ess'; To = 'HTTPS' },
  @{ From = 'dee en ess';     To = 'DNS' },
  @{ From = 'see dee en';     To = 'CDN' },
  @{ From = 'ay double you ess'; To = 'AWS' }
)

$nl = "`r`n"

$snippets = @(
  @{ Trigger = 'my signature'; Text = "Michael Burns${nl}Jupitor Studio${nl}tawktype.com" },
  @{ Trigger = 'my address';   Text = "Jupitor Studio${nl}1 Example Street${nl}Dublin, Ireland" },
  @{ Trigger = 'bug template'; Text = "**What I did**${nl}${nl}**What I expected**${nl}${nl}**What happened**${nl}" },
  @{ Trigger = 'release checklist'; Text = "- tests green${nl}- handoff updated${nl}- tag pushed${nl}- release notes read once" },
  @{ Trigger = 'standup';      Text = "Yesterday:${nl}Today:${nl}Blocked by:" },
  @{ Trigger = 'thanks note';  Text = 'Thanks very much for this - I will take a look and come back to you.' },
  @{ Trigger = 'out of office'; Text = "I am away from my desk and will reply when I am back.${nl}${nl}Michael" },
  @{ Trigger = 'repo link';    Text = 'https://github.com/JupitorStudioDev/TawkType' },
  @{ Trigger = 'licence line'; Text = 'Licensed under Apache-2.0. See LICENSE and NOTICE.' },
  @{ Trigger = 'commit trailer'; Text = 'Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>' },
  @{ Trigger = 'meeting notes'; Text = "Present:${nl}Decisions:${nl}Actions:" },
  @{ Trigger = 'pr description'; Text = "**What this changes**${nl}${nl}**Why**${nl}${nl}**How it was checked**" },
  @{ Trigger = 'sql select';   Text = 'select * from dictations order by created_at desc limit 50;' },
  @{ Trigger = 'ps list';      Text = 'Get-Process -Name TawkType | Select-Object Id, StartTime' },
  @{ Trigger = 'build command'; Text = 'dotnet build TawkType.sln' },
  @{ Trigger = 'test command'; Text = 'dotnet test' },
  @{ Trigger = 'log path';     Text = '%LOCALAPPDATA%\TawkType\logs\tawktype.log' },
  @{ Trigger = 'data path';    Text = '%LOCALAPPDATA%\TawkType' },
  @{ Trigger = 'apology';      Text = 'Sorry for the slow reply - it has been a busy week.' },
  @{ Trigger = 'sign off';     Text = "Best,${nl}Michael" }
)

$payload = [ordered]@{
  Spellings    = $spellings
  Replacements = $replacements
  Snippets     = $snippets
}

$json = $payload | ConvertTo-Json -Depth 5
Set-Content -LiteralPath $Out -Value $json -Encoding utf8

Write-Output "wrote $Out"
Write-Output ("{0} spellings, {1} replacements, {2} snippets" -f $spellings.Count, $replacements.Count, $snippets.Count)
