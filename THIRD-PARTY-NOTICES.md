# Avisos de terceiros

O Lotus usa ou se inspira nos projetos abaixo. Cada um mantém a própria licença.

## Controle de cores NVIDIA

`WindowsMonitorColors` usa como referência de interoperabilidade os exemplos DVC Ex de
**NVIDIA_NvAPI**, jNizM, commit `b29bd4fb711c38982a07599c509b6945db69836d`:
https://github.com/jNizM/NVIDIA_NvAPI . Nenhum executável ou DLL desse projeto é distribuído.

MIT License

Copyright (c) 2021 jNizM

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Referência de design

- **ResetExplode**, por Gustavo (GustavoReset), licença MIT: https://github.com/GustavoReset/ResetExplode
  Usado como referência técnica para descoberta de jogos, leitura de inicialização e recursos de jogos do Windows. O ResetExplode não é distribuído com o Lotus. Veja `docs/LOTUS_RESETEXPLODE_REFERENCE.md`.
  > Copyright (c) 2026 Gustavo (GustavoReset). Permission is hereby granted, free of charge, to any person obtaining a copy of this software ... (texto completo da licença MIT no link acima).

- **CapFrameX**, por DevTechProfile (CXWorld), licença MIT: https://github.com/CXWorld/CapFrameX
  Ideia da checagem de plano de vídeo extra (MPO) e da leitura do modo de apresentação do PresentMon, reescritas para o Lotus a partir da documentação do Windows (`MpoProbe`, `PresentModes`). O CapFrameX não é distribuído com o Lotus.
  > Copyright (c) 2018-2026 DevTechProfile. Permission is hereby granted, free of charge, to any person obtaining a copy of this software ... (texto completo da licença MIT no link acima).

## Dependências (NuGet)

- Microsoft.WindowsAppSDK e Microsoft.Windows.SDK.BuildTools: licença da Microsoft (MIT / Windows SDK).
- Microsoft.Extensions.DependencyInjection: MIT.
- CommunityToolkit.Mvvm: MIT.
- Serilog: Apache-2.0.
- Microsoft.Data.Sqlite / SQLitePCLRaw: MIT / Apache-2.0.
- Vortice.Direct3D11 / Vortice.D3DCompiler: MIT.
- System.Management / System.Drawing.Common: MIT.
- xUnit (somente testes): Apache-2.0.

## Ferramentas e dados usados em tempo de execução

- **Ludusavi Manifest**, por Matthew T. Kennerly (mtkennerly), licença MIT: https://github.com/mtkennerly/ludusavi-manifest
  Baixado sob demanda (no máximo uma vez por semana) para saber onde ficam os saves de milhares de jogos. Os dados vêm da **PCGamingWiki** (CC BY-NC-SA 3.0), https://www.pcgamingwiki.com. O Lotus guarda só um índice das pastas de save do Windows.

- **PresentMon** (Intel/GameTechDev, MIT): baixado sob demanda para medir FPS.
- **RivaTuner Statistics Server** (Unwinder/Guru3D): integração opcional para o overlay; não é distribuído com o Lotus.
- **HowLongToBeat** (howlongtobeat.com): tempos para zerar consultados no site (só o título do jogo é enviado), do mesmo jeito que o plugin de código aberto **playnite-howlongtobeat-plugin**, por Lacro59, licença MIT: https://github.com/Lacro59/playnite-howlongtobeat-plugin (referência, sem código copiado).
- Dados públicos da **Steam**, **SteamSpy**, **GOG** e **mygamelist.club** para listas, requisitos e notas.
- Estimativas de FPS inspiradas em **pc-builds.com**, **How Many FPS** e **3DMark**, citados como fontes no app.
