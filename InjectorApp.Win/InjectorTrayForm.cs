using EasyHook;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace InjectorApp.Win
{
    public partial class InjectorTrayForm : Form
    {

        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly ToolStripMenuItem _startMenuItem;
        private readonly ToolStripMenuItem _stopMenuItem;
        private readonly ToolStripMenuItem _exitMenuItem;
        private readonly Timer _timer;
        private readonly HashSet<int> _knownPids = new HashSet<int>();

        private bool _running = false;

        private const string TargetProcessName = "interativo"; // sem .exe
        private readonly string _dllPath;

        public InjectorTrayForm()
        {
            InitializeComponent();
            // Caminho da DLL (mesma pasta do executável)
            _dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChangeBgPayload.dll");

            // Esconde o form, usamos só o NotifyIcon
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            Load += (s, e) => { Hide(); };

            // Context menu da bandeja
            _contextMenu = new ContextMenuStrip();

            _startMenuItem = new ToolStripMenuItem("Iniciar", null, OnStartClicked);
            _stopMenuItem = new ToolStripMenuItem("Parar", null, OnStopClicked)
            {
                Enabled = false
            };
            _exitMenuItem = new ToolStripMenuItem("Sair", null, OnExitClicked);

            _contextMenu.Items.Add(_startMenuItem);
            _contextMenu.Items.Add(_stopMenuItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(_exitMenuItem);

            // NotifyIcon
            _notifyIcon = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                //Icon = SystemIcons.Application, // coloque seu próprio .ico se quiser
                Visible = true,
                Text = "InjectorApp.Win",
                ContextMenuStrip = _contextMenu
            };

            _notifyIcon.DoubleClick += (s, e) =>
            {
                // Atalho: duplo clique alterna iniciar/parar
                if (!_running)
                    StartMonitoring();
                else
                    StopMonitoring();
            };

            // Timer de monitoramento
            _timer = new Timer
            {
                Interval = 4000 // 4 segundos
            };
            _timer.Tick += Timer_Tick;
        }

        private void OnStartClicked(object sender, EventArgs e)
        {
            StartMonitoring();
        }

        private void OnStopClicked(object sender, EventArgs e)
        {
            StopMonitoring();
        }

        private void OnExitClicked(object sender, EventArgs e)
        {
            _notifyIcon.Visible = false;
            _timer.Stop();
            Application.Exit();
        }

        private void StartMonitoring()
        {
            if (_running)
                return;

            if (!File.Exists(_dllPath))
            {
                ShowBalloon("DLL não encontrada",
                    $"Não foi encontrada a DLL em:\n{_dllPath}",
                    ToolTipIcon.Error);
                return;
            }

            _running = true;
            _startMenuItem.Enabled = false;
            _stopMenuItem.Enabled = true;

            // Limpa os PIDs conhecidos para considerar os atuais como "novos" e injetar
            _knownPids.Clear();

            _timer.Start();

            ShowBalloon("Monitoramento iniciado",
                $"Vigiando novos processos \"{TargetProcessName}.exe\" para injetar ChangeBgPayload.dll.",
                ToolTipIcon.Info);
        }

        private void StopMonitoring()
        {
            if (!_running)
                return;

            _running = false;
            _timer.Stop();

            _startMenuItem.Enabled = true;
            _stopMenuItem.Enabled = false;

            ShowBalloon("Monitoramento parado",
                "Novos processos não serão mais injetados.",
                ToolTipIcon.Info);
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!_running)
                return;

            try
            {
                var procs = Process.GetProcessesByName(TargetProcessName);
                
                // Descobre novos PIDs
                foreach (var proc in procs)
                {
                    if (!_knownPids.Contains(proc.Id))
                    {
                        // Ainda não vimos esse PID
                        TryInject(proc);
                    }
                }

                // Remove PIDs que já morreram
                var currentIds = new HashSet<int>(procs.Select(p => p.Id));
                _knownPids.RemoveWhere(id => !currentIds.Contains(id));
            }
            catch (Exception ex)
            {
                // Em caso de erro geral, só mostra um balão uma vez
                ShowBalloon("Erro no monitoramento", ex.Message, ToolTipIcon.Error);
            }
        }

        private void TryInject(Process target)
        {
            try
            {
                // Marca como visto antes de injetar, para evitar loop em caso de erro
                _knownPids.Add(target.Id);

                RemoteHooking.Inject(
                    target.Id,
                    InjectionOptions.Default,
                    _dllPath, // 32-bit library
                    _dllPath, // 64-bit library (se tivesse outra, poderia passar aqui)
                    "channelName" // argumento para o EntryPoint da DLL
                );

                ShowBalloon(
                    "Injeção realizada",
                    $"DLL injetada no processo {TargetProcessName}.exe (PID {target.Id}).",
                    ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                ShowBalloon(
                    "Falha na injeção",
                    $"Não foi possível injetar no PID {target.Id}:\n{ex.Message}",
                    ToolTipIcon.Error);
            }
        }

        private void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            try
            {
                _notifyIcon.BalloonTipTitle = title;
                _notifyIcon.BalloonTipText = text;
                _notifyIcon.BalloonTipIcon = icon;
                _notifyIcon.ShowBalloonTip(3000);
            }
            catch
            {
                // Ignora qualquer erro de balloon
            }
        }


        //protected override void Dispose(bool disposing)
        //{
        //    if (disposing)
        //    {
        //        _timer?.Dispose();
        //        _notifyIcon?.Dispose();
        //        _contextMenu?.Dispose();
        //    }
        //    base.Dispose(disposing);
        //}
    }
}