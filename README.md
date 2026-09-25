# Virtual Joy-Con — projeto JoyLiveEX

**Transforme seu PC em um controle virtual para Android.** Dois Joy-Cons virtuais no
Windows (mouse **e** touch, teclado, gamepad físico opcional) enviam eventos de gamepad
para o celular por **USB (ADB)** ou **Internet/LAN** — sem controle físico obrigatório,
sem servidor pago obrigatório, sem simulação vazia.

> ⚠️ Projeto independente, sem afiliação com a Nintendo. O layout é inspirado no
> conceito de "dois controles separados"; nenhum logo ou identidade proprietária é usada.

---

## O que é real aqui

| Pedido no brief | Estado |
|---|---|
| Botões clicáveis com DOWN/UP reais | ✅ eventos disparados no press/release + anti-"stuck" por captura de ponteiro |
| Analógicos arrastáveis −1.0..+1.0 com retorno ao centro | ✅ mouse/touch, deadzone, sensibilidade, curva, inversão Y, intensidade máx. |
| Teclado remapeável (W A S D, J K U I, Q E, 1 2, Enter/Backspace…) | ✅ todas as ações remapeáveis por captura de tecla |
| Gamepad físico no PC detectado automaticamente (XInput) | ✅ slot auto-detect, mapeável por captura de botão |
| USB via ADB (detecção, autorização, erro, queda) | ✅ `adb devices -l` + `adb reverse` + daemon de entrada via `app_process` |
| Pareamento por código na Internet | ✅ código 8 caracteres, sessão PBKDF2+AES-GCM, janela anti-replay, rate-limit |
| Sequência, heartbeat, reconexão, ping, perda, anti-estado-preso | ✅ protocolo próprio binário (UDP; TCP no USB) |
| P2P direto com relay de fallback | ✅ relay mínimo self-host (`VjcRelay`) incluído; upgrade P2P automático quando possível |
| Injeção global como gamepad no Android (`SOURCE_GAMEPAD`, `AXIS_X/Y/Z/RZ`, `AXIS_LTRIGGER`…) | ✅ via **daemon rodando como shell pela ADB** (o caminho tecnicamente correto — APK comum não pode); fallback **Acessibilidade → toque**; modo **in-app** para testes |
| Teste de compatibilidade no app | ✅ tela CONTROLLER TEST lendo InputEvents reais |
| EXE compilável + APK + GitHub Actions + artefatos `VirtualJoyCon-Windows.zip` / `VirtualJoyCon-Android.apk` | ✅ workflow `.github/workflows/build.yml` (builds, unit tests, draft release) |
| Nada instalado silenciosamente | ✅ adb ausente ⇒ instruções; daemon só sob demanda explícita (botão INICIAR) |

Detalhes técnicos de **por que** a injeção global usa o daemon ADB e o que o Android
permit ou não: [`docs/ANDROID-INPUT-MODES.md`](docs/ANDROID-INPUT-MODES.md).

## Arquitetura

```
 PC (Windows EXE, .NET 8 / WPF)                    Android
┌─────────────────────────────┐        ┌──────────────────────────────────────┐
│ DesktopApp (WPF UI)         │        │ APK companion (Kotlin)               │
│  ├─ InputEngine (KB/pad/UX) │        │  ├─ ui/ (Main, Controller, Pair,     │
│  ├─ ControllerModel (estado)│        │  │   Test, Settings, Diagnostics)    │
│  ├─ Configuration (JSON)     │        │  ├─ controller/ (state, curva, a11y)│
│  ├─ Network (protocolo VJC) │◄─UDP──►│  ├─ network/ (espelho do protocolo)  │
│  ├─ UsbAdb (adb+daemon)     │◄─TCP──►│  ├─ input/ (A11Y touch, router)      │
│  └─ Diagnostics (log/stats) │  USB   │  └─ diagnostics/ (EventLog)          │
└──────────────┬──────────────┘        │           ┌──────────────────┐       │
               │ adb reverse + push     │  daemon   │ vjc-daemon.jar   │       │
               └────────────────────────┼─────────►│ (Java/shell uid — │       │
                                        │          │ SOURCE_GAMEPAD    │       │
                                        │          │ global inject)     │       │
                                        │          └─────────┬──────────┘       │
                                        │                    ▼                 │
                                        │        InputManager → JOGO (gamepad) │
                                        └──────────────────────────────────────┘
```

- **USB**: o EXE usa o *seu* `adb` (nada é baixado), faz `adb reverse` + push do
  `vjc-daemon.jar` e roda o daemon como **shell** — exatamente a arquitetura do scrcpy.
  O daemon injeta `KeyEvent`/`MotionEvent` com `SOURCE_GAMEPAD` no sistema inteiro.
- **LAN/Internet**: UDP binário (~20 B por estado), 125 Hz + envio imediato por botão;
  relay só encaminha (payload cifrado ponta a ponta); P2P direto quando o NAT permitir.
- O próprio celular vira um controle: a tela CONTROLES do APK tem os dois Joy-Cons e
  os eventos voltam ao PC (fonte de entrada adicional, com multitouch real).

## Primeiros passos (5 minutos)

### USB (maior compatibilidade)
1. Instale as **Platform Tools** você mesmo: `winget install Google.PlatformTools`
   (ou aponte o caminho nas Configurações → CONEXÃO).
2. No celular: Opções do desenvolvedor → **Depuração USB** → conecte o cabo →
   autorize "Permitir depuração USB?".
3. Abra `VirtualJoyCon.exe` → modo **USB** → *Conectar dispositivo*.
4. Instale/abra o app **Virtual Joy-Con** no celular (ele conecta sozinho no modo USB
   assim que o serviço inicia) e toque em **INICIAR**.
5. No PC: **INICIAR CONTROLE** → o daemon é enviado e a injeção global de gamepad
   liga. Abra um jogo com suporte a gamepad e jogue.
6. Valide com **CONTROLLER TEST** no celular (com o teste focado, os eventos chegam
   como InputEvents de gamepad reais).

### Internet / LAN
1. No PC: LAN local = pronto (código na tela). Internet = informe um relay próprio
   (Configurações → REDE) e rode `VjcRelay.exe 8722` em qualquer máquina com UDP
   liberado (Docker pronto em `desktop/tools/VjcRelay/Dockerfile`).
2. No celular: **PAREAMENTO** → LAN (BUSCAR LAN!) ou INTERNET → cole o código
   `XXXX-XXXX` → CONECTAR.

## Build local

- Windows: `dotnet build JoyLiveEX.sln` / `dotnet publish …` (detalhes:
  [`docs/BUILD.md`](docs/BUILD.md)). O `vjc-daemon.jar` é gerado pelo Gradle
  (`android/:daemon:dexJar`) — precisa do Android SDK (d8) apenas para o daemon.
- Android: `cd android && ./gradlew :app:assembleDebug :daemon:dexJar`.
- CI: push → GitHub Actions roda testes + build Windows + build APK e publica
  artefatos `VirtualJoyCon-Windows.zip` e `VirtualJoyCon-Android.apk`.

## Estrutura

```
desktop/  src/{DesktopApp(WPF), ControllerModel, InputEngine, Network, UsbAdb,
              VirtualController, Configuration, Diagnostics}  tools/VjcRelay/
          tests/VirtualJoyCon.Core.Tests/
android/  app/(UI, controller, network, input, diagnostics, config)  daemon/(Java → dex)
docs/     ARCHITECTURE.md PROTOCOL.md ANDROID-INPUT-MODES.md BUILD.md TESTVECTORS.md
```

## Segurança

- Código de pareamento nunca é a única proteção: deriva chave PBKDF2-HMAC-SHA256
  (60k it) com salt efêmero dos dois lados; **tudo** pós-handshake é AES-GCM
  (autenticado), com janela de replay por sequência.
- 8 tentativas de auth/10 s ⇒ rate limit; sessão substitui par antigo; timeout +
  fechamento; reconexão re-autentica do zero (nova derivada, forward secrecy *casual*).
- O relay não decifra nada (apenas envelope `VJC1|op|room`), não guarda log de payload.
- LAN sem código = código padrão da rede local (`JOYLIVE0`); na Internet o código é
  sempre obrigatório.

## Limitações assumidas (sem fingimento)

- APK comum **não pode** criar um dispositivo HID global — por isso o modo gamepad
  global exige o daemon via ADB (root não é necessário; root *não* é usado).
- Em redes com NAT simétrico estrito o P2P pode falhar → tráfego segue pelo relay.
- Jogos com detecção anti-hack agressiva podem ignorar eventos injetados (restrição
  do próprio Android — o teste de compatibilidade mostra o que chegou).
- Keyboard map padrão: o brief atribuía `I/J/K/L` a duas funções; priorizamos
  face buttons (`J=A, K=B, U=X, I=Y`) e o D-Pad ficou com as setas — tudo remapeável.

## Licença / marcas

MIT (código). "Joy-Con" e "Nintendo Switch" são marcas da Nintendo Co., Ltd. —
este projeto não é afiliado, endossado ou patrocinado por ela.
