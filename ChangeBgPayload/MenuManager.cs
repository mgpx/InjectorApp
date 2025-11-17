using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ChangeBgPayload
{
    internal static class MenuManager
    {
        // ===== Menus & MessageBox =====
        private const int WM_COMMAND = 0x0111;
        private const uint MF_STRING = 0x0000;
        private const uint MF_POPUP = 0x0010;
        private const uint MF_BYPOSITION = 0x0400;

        // ID do nosso item "Config Cor"
        public const int IDM_CONFIG_COR = 0x9001;

        [DllImport("user32.dll")]
        private static extern IntPtr GetMenu(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetMenuItemCount(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern IntPtr GetSubMenu(IntPtr hMenu, int nPos);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMenuStringW")]
        private static extern int GetMenuStringW(
            IntPtr hMenu,
            uint uIDItem,
            StringBuilder lpString,
            int cchMax,
            uint uFlag);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "AppendMenuW")]
        private static extern bool AppendMenuW(
            IntPtr hMenu,
            uint uFlags,
            UIntPtr uIDNewItem,
            string lpNewItem);

        [DllImport("user32.dll")]
        private static extern bool DrawMenuBar(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
        private static extern int MessageBoxW(
            IntPtr hWnd,
            string lpText,
            string lpCaption,
            uint uType);

        private static int LOWORD(IntPtr value)
            => unchecked((short)((long)value & 0xFFFF));

        private static bool _installed;

        /// <summary>
        /// Verifica se o menu principal já tem um item top-level "Ajustes".
        /// </summary>
        private static bool HasAjustesMenu(IntPtr hMainMenu)
        {
            if (hMainMenu == IntPtr.Zero)
                return false;

            int count = GetMenuItemCount(hMainMenu);
            if (count <= 0)
                return false;

            var sb = new StringBuilder(256);

            for (int i = 0; i < count; i++)
            {
                sb.Clear();
                int len = GetMenuStringW(hMainMenu, (uint)i, sb, sb.Capacity, MF_BYPOSITION);
                if (len <= 0)
                    continue;

                string text = sb.ToString().Trim('&').Trim();
                if (text.Equals("AJUSTES", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Garante que exista um menu top-level "Ajustes" com item "Config Cor".
        /// Pode ser chamado várias vezes; se já existir, não faz nada.
        /// </summary>
        public static void EnsureAjustesMenu(IntPtr hWnd, Action<string> log)
        {
            if (hWnd == IntPtr.Zero)
                return;

            try
            {
                IntPtr hMainMenu = GetMenu(hWnd);
                if (hMainMenu == IntPtr.Zero)
                {
                    // Form sem menu principal
                    return;
                }

                // Se já tem "Ajustes", não faz nada
                if (HasAjustesMenu(hMainMenu))
                    return;

                // Cria submenu de "Ajustes"
                IntPtr hSub = CreatePopupMenu();
                if (hSub == IntPtr.Zero)
                {
                    log?.Invoke("CreatePopupMenu falhou ao criar submenu Ajustes");
                    return;
                }

                // Item "Config Cor" dentro de "Ajustes"
                if (!AppendMenuW(
                    hSub,
                    MF_STRING,
                    (UIntPtr)(uint)IDM_CONFIG_COR,
                    "Config Cor"))
                {
                    log?.Invoke("Falha ao adicionar item 'Config Cor' em Ajustes");
                    return;
                }

                // Top-level "Ajustes" na barra de menu
                // Para MF_POPUP, uIDNewItem recebe o handle do submenu.
                // Conversão IntPtr -> UIntPtr levando em conta 32/64 bits:
                UIntPtr subHandle = new UIntPtr((ulong)hSub.ToInt64());

                if (!AppendMenuW(
                    hMainMenu,
                    MF_STRING | MF_POPUP,
                    subHandle,
                    "Ajustes"))
                {
                    log?.Invoke("Falha ao adicionar menu top-level 'Ajustes'");
                    return;
                }

                DrawMenuBar(hWnd);
                log?.Invoke("Menu 'Ajustes' com 'Config Cor' criado");
            }
            catch (Exception ex)
            {
                log?.Invoke("Erro em EnsureAjustesMenu: " + ex);
            }
        }


    }

}
