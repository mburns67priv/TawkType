# Third-party notices

TawkType is built on the work below. Licence details were taken from each package's own metadata.

## Speech recognition models

TawkType does **not** redistribute either model. Both are downloaded from Hugging Face on first use and
stored in `%LOCALAPPDATA%\Jupitor Studio\TawkType\models`. Neither is modified.

### NVIDIA Parakeet TDT 0.6B v3

- Creator: **NVIDIA Corporation**
- Licence: **Creative Commons Attribution 4.0 International (CC BY 4.0)** —
  <https://creativecommons.org/licenses/by/4.0/>
- Model: <https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3>
- Used unmodified, via the ONNX int8 export published by **csukuangfj** (k2-fsa):
  <https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8>

CC BY 4.0 permits commercial use, redistribution and modification provided NVIDIA is credited, the
licence is linked, and any changes are stated. TawkType credits NVIDIA here, in the README and in the
application itself (Settings → General → About).

> If you fork TawkType and bundle the model weights with an installer, you are then *sharing* the
> material, and CC BY 4.0's attribution terms apply to your distribution as well — carry this notice
> with it.

### OpenAI Whisper (large-v3-turbo and other sizes)

- Creator: **OpenAI**
- Licence: **MIT**
- Model: <https://huggingface.co/openai/whisper-large-v3-turbo>
- Downloaded in the GGML format published by the whisper.cpp project.

## Runtime libraries

| Package | Licence | Project |
|---|---|---|
| Whisper.net, Whisper.net.Runtime (+ Vulkan, Cuda12) | MIT — © 2024 sandrohanea | <https://github.com/sandrohanea/whisper.net> |
| org.k2fsa.sherpa.onnx | Apache-2.0 | <https://github.com/k2-fsa/sherpa-onnx> |
| NAudio | MIT — © 2020 Mark Heath | <https://github.com/naudio/NAudio> |
| H.NotifyIcon.Wpf | MIT | <https://github.com/HavenDV/H.NotifyIcon> |
| CommunityToolkit.Mvvm | MIT | <https://github.com/CommunityToolkit/dotnet> |
| Anthropic .NET SDK | MIT | <https://github.com/anthropics/anthropic-sdk-csharp> |
| Microsoft.Extensions.* (Hosting, Logging) | MIT | <https://github.com/dotnet/runtime> |
| System.Security.Cryptography.ProtectedData | MIT | <https://github.com/dotnet/runtime> |

Whisper.net bundles builds of **whisper.cpp** (MIT, © 2023 Georgi Gerganov), and sherpa-onnx bundles
**ONNX Runtime** (MIT, © Microsoft Corporation).

## Test-only

| Package | Licence |
|---|---|
| xunit, xunit.runner.visualstudio | Apache-2.0 |
| Microsoft.NET.Test.Sdk | MIT |

## TawkType itself

TawkType carries no licence at this time, which means all rights are reserved by Jupitor Studio. You may
read the source; you do not have permission to use, copy, modify or distribute it. If you want to do any
of those things, open an issue and ask.
