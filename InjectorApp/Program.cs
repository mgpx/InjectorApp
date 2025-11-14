using EasyHook;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InjectorApp
{
    public class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                args = new[] { "interativo.exe", "ChangeBgPayload.dll" };
            }

            if (args.Length < 2)
            {
                Console.WriteLine("Uso: Injector.exe <processName.exe> <caminho\\ChangeBgPayload.dll>");
                return;
            }

            string procName = args[0];
            string dllPath = args[1];

            if (!File.Exists(dllPath))
            {
                Console.WriteLine("DLL não encontrada: " + dllPath);
                return;
            }

            var procs = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(procName));
            if (procs.Length == 0)
            {
                Console.WriteLine("Processo não encontrado: " + procName);
                return;
            }

            // escolher o primeiro processo (ou permitir seleção)
            var target = procs[0];
            Console.WriteLine($"PID {target.Id} selecionado (nome {target.ProcessName})");

            try
            {
                // Injetar com EasyHook (versão simples)
                // O segundo parâmetro é opcional para argumentos passados ao constructor do EntryPoint
                RemoteHooking.Inject(
                    target.Id,
                    InjectionOptions.Default,
                    dllPath,     // 32-bit library (se o processo for 32-bit)
                    dllPath,     // 64-bit library (se o processo for 64-bit)
                                 // argumentos que serão passados ao constructor EntryPoint(context, args...)
                    "channelName"
                );

                Console.WriteLine("Injeção solicitada com sucesso.");
                


            }
            catch (Exception ex)
            {
                Console.WriteLine("Falha na injeção: " + ex);
            }

            Console.ReadKey();
        }
    }
}
