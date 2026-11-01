param([Parameter(Mandatory=$true)][string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
# A second apphost loads the same managed assembly, but uses the console subsystem.
# With CreateNoWindow this worker has neither a console window nor GUI startup feedback.
# Keep the GUI apphost unchanged for normal launches from Explorer.
$taskBytes = [IO.File]::ReadAllBytes($ExecutablePath)
$taskPeOffset = [BitConverter]::ToInt32($taskBytes, 0x3c)
if ([BitConverter]::ToUInt32($taskBytes, $taskPeOffset) -ne 0x4550) { throw 'Invalid PE signature.' }
$taskMagic = [BitConverter]::ToUInt16($taskBytes, $taskPeOffset + 24)
if ($taskMagic -ne 0x20b -and $taskMagic -ne 0x10b) { throw 'Unsupported PE optional header.' }
$taskSubsystemOffset = $taskPeOffset + 24 + 68
if ([BitConverter]::ToUInt16($taskBytes, $taskSubsystemOffset) -ne 2) { throw 'Expected Windows GUI apphost.' }
$taskBytes[$taskSubsystemOffset] = 3
$taskBytes[$taskSubsystemOffset + 1] = 0
$taskWorkerPath = Join-Path ([IO.Path]::GetDirectoryName($ExecutablePath)) 'GDesktop.Extractor.exe'
[IO.File]::WriteAllBytes($taskWorkerPath, $taskBytes)
