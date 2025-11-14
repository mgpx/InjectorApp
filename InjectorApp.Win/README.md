# ChangeBg Injector

Conjunto de ferramentas para injeção de uma DLL (`ChangeBgPayload.dll`) em outro processo, com o objetivo principal de alterar a cor de fundo da aplicação (especialmente grids como `TSQLGrid`) para um tom mais agradável (bege), sem precisar alterar o código-fonte original.

## 📋 Componentes

Este repositório contém:

- **ChangeBgPayload.dll** – DLL injetada no processo (via EasyHook), responsável por interceptar chamadas de desenho (GDI/WinAPI) e trocar o fundo branco pelo fundo configurado.
- **InjectorApp.Win** – Aplicativo Windows que roda na bandeja do sistema (systray) e injeta automaticamente a DLL sempre que o processo-alvo é iniciado.

## 🖥️ InjectorApp.Win

### O que ele faz

InjectorApp.Win é um aplicativo WinForms minimalista que:

1. Fica residente na bandeja do sistema (perto do relógio)
2. Monitora periodicamente (a cada 2 segundos) a existência do processo configurado (`interativo.exe` por padrão)
3. Sempre que detecta um novo PID do processo-alvo, chama `EasyHook.RemoteHooking.Inject` para injetar a `ChangeBgPayload.dll`
4. Mostra notificações (balloons) informando:
   - Início/parada do monitoramento
   - Sucesso ou falha na injeção
   - Erros de monitoramento

### Interface (bandeja)

- **Menu de contexto** (botão direito no ícone):
  - **Iniciar** – Começa a vigiar o processo e injetar a DLL
  - **Parar** – Interrompe o monitoramento
  - **Sair** – Encerra o aplicativo e remove o ícone da bandeja
- **Duplo clique no ícone:** Alterna entre Iniciar e Parar o monitoramento

### Configurações principais

No código:

```csharp
private const string TargetProcessName = "interativo"; // sem .exe

// DLL a ser injetada (deve estar na mesma pasta do executável)
_dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChangeBgPayload.dll");
```

- **TargetProcessName:** Nome do processo-alvo sem ".exe". Se o executável mudar de nome, basta ajustar essa constante.
- **_dllPath:** Caminho da DLL a ser injetada. Por padrão, é a mesma pasta em que o `InjectorApp.Win.exe` está sendo executado.

### Funcionamento interno

1. O usuário inicia o `InjectorApp.Win`
2. O aplicativo cria:
   - Um `NotifyIcon` na bandeja
   - Um `Timer` com intervalo de 4000 ms
3. Ao clicar em **Iniciar**:
   - Verifica se `ChangeBgPayload.dll` existe
   - Limpa a lista interna de PIDs conhecidos (`_knownPids`)
   - Inicia o `Timer`
4. A cada tick:
   - Obtém todos os processos com o nome `TargetProcessName`
   - Para cada PID ainda não visto, chama `TryInject`:

```csharp
RemoteHooking.Inject(
    target.Id,
    InjectionOptions.Default,
    _dllPath, // 32-bit
    _dllPath, // 64-bit
    "channelName"
);
```

   - Registra o PID em `_knownPids` para não tentar injetar novamente
   - Remove da lista os PIDs de processos que já terminaram

## 🔧 Pré-requisitos

- Windows (testado em versões modernas, como 11)
- .NET compatível com WinForms + EasyHook (por exemplo .NET Framework 4.7.2)
- Biblioteca **EasyHook** referenciada no projeto `InjectorApp.Win`
- `ChangeBgPayload.dll` já compilada e presente na mesma pasta do `InjectorApp.Win.exe`

## ⚙️ Instalação e Uso

1. Compile os projetos `ChangeBgPayload` e `InjectorApp.Win`
2. Certifique-se de que `ChangeBgPayload.dll` está na mesma pasta que `InjectorApp.Win.exe`
3. Execute `InjectorApp.Win.exe`
4. Clique com o botão direito no ícone da bandeja e selecione **Iniciar**
5. Inicie o processo-alvo (por padrão, `interativo.exe`)
6. A DLL será injetada automaticamente e você verá uma notificação de sucesso

## ⚠️ Aviso Importante

Este projeto faz **injeção de código em outro processo**, o que:

- Pode ser bloqueado por antivírus
- Pode causar instabilidade/erros se a DLL estiver incorreta
- Deve ser usado apenas em ambientes controlados (sistemas internos, testes, laboratório)

**Use por sua conta e risco**, sempre respeitando licenças e políticas de uso do software alvo.



## 🤝 Contribuições

Contribuições são bem-vindas! Sinta-se à vontade para abrir issues ou pull requests.
