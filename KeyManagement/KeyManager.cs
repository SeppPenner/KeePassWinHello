﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using KeePass.Forms;
using KeePassLib.Keys;
using KeePassLib.Serialization;

namespace KeePassWinHello
{
    class KeyManager
    {
        private readonly KeyCipher _keyCipher;
        private readonly KeyStorage _keyStorage;

        public KeyManager(IntPtr windowHandle)
        {
            _keyStorage = new KeyStorage();
            _keyCipher = new KeyCipher(Settings.ConfirmationMessage, windowHandle);
        }

        public void OnKeyPrompt(KeyPromptForm keyPromptForm)
        {
            if (keyPromptForm.SecureDesktopMode)
                return;

            if (!Settings.Instance.Enabled)
                return;

            CompositeKey compositeKey;
            if (ExtractCompositeKey(GetDbPath(keyPromptForm), out compositeKey))
            {
                SetCompositeKey(keyPromptForm, compositeKey);
                // Remove flushing
                keyPromptForm.Visible = false;
                keyPromptForm.Opacity = 0;

                keyPromptForm.DialogResult = DialogResult.OK;
                keyPromptForm.Close();
            }
        }

        public void OnOptionsLoad(OptionsForm optionsForm)
        {
            OptionsPanel.AddTab(GetTabControl(optionsForm), GetTabsImageList(optionsForm), _keyCipher.IsAvailable);
        }

        public void OnDBClosing(object sender, FileClosingEventArgs e)
        {
            if (e == null)
            {
                Debug.Fail("Event is null");
                return;
            }

            if (e.Cancel || e.Database == null || e.Database.MasterKey == null || e.Database.IOConnectionInfo == null)
                return;

            string dbPath = e.Database.IOConnectionInfo.Path;
            if (!IsDBLocking(e))
            {
                _keyStorage.Remove(dbPath);
            }
            else if (AuthProviderFactory.IsAvailable() && Settings.Instance.Enabled)
            {
                _keyStorage.AddOrUpdate(dbPath, ProtectedKey.Create(e.Database.MasterKey, _keyCipher));
            }
        }

        private bool ExtractCompositeKey(string dbPath, out CompositeKey compositeKey)
        {
            compositeKey = null;

            if (String.IsNullOrEmpty(dbPath))
                return false;

            ProtectedKey encryptedData;
            if (!_keyStorage.TryGetValue(dbPath, out encryptedData))
                return false;

            try
            {
                compositeKey = encryptedData.GetCompositeKey(_keyCipher);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                _keyStorage.Remove(dbPath);
            }
            catch (Exception ex)
            {
                Debug.Fail(ex.ToString()); // TODO: fix canceled exception
                _keyStorage.Remove(dbPath);
            }
            return false;
        }

        private static void SetCompositeKey(KeyPromptForm keyPromptForm, CompositeKey compositeKey)
        {
            var fieldInfo = keyPromptForm.GetType().GetField("m_pKey", BindingFlags.Instance | BindingFlags.NonPublic);
            if (fieldInfo != null)
                fieldInfo.SetValue(keyPromptForm, compositeKey);
        }

        private static bool IsDBLocking(FileClosingEventArgs e)
        {
            try
            {
                var FlagsProperty = typeof(FileClosingEventArgs).GetProperty("Flags");
                if (FlagsProperty == null)
                    return true;

                var FlagsType = FlagsProperty.PropertyType;
                int FlagsValue = Convert.ToInt32(FlagsProperty.GetValue(e, null));

                var names = Enum.GetNames(FlagsType);
                for (int i = 0; i != names.Length; ++i)
                {
                    if (names[i] == "Locking")
                    {
                        int Locking = Convert.ToInt32(Enum.GetValues(FlagsType).GetValue(i));
                        if ((FlagsValue & Locking) != Locking)
                        {
                            return false;
                        }
                        break;
                    }
                }
            }
            catch { }
            return true;
        }

        private static string GetDbPath(KeyPromptForm keyPromptForm)
        {
            var ioInfo = GetIoInfo(keyPromptForm);
            if (ioInfo == null)
                return null;
            return ioInfo.Path;
        }

        private static IOConnectionInfo GetIoInfo(KeyPromptForm keyPromptForm)
        {
            var fieldInfo = keyPromptForm.GetType().GetField("m_ioInfo", BindingFlags.Instance | BindingFlags.NonPublic);
            if (fieldInfo == null)
                return null;
            return fieldInfo.GetValue(keyPromptForm) as IOConnectionInfo;
        }

        private static TabControl GetTabControl(OptionsForm optionsForm)
        {
            return optionsForm.Controls.Find("m_tabMain", true).FirstOrDefault() as TabControl;
        }

        private static ImageList GetTabsImageList(OptionsForm optionsForm)
        {
            var m_ilIconsField = optionsForm.GetType().GetField("m_ilIcons", BindingFlags.Instance | BindingFlags.NonPublic);
            if (m_ilIconsField == null)
                return null;
            return m_ilIconsField.GetValue(optionsForm) as ImageList;
        }
    }
}
