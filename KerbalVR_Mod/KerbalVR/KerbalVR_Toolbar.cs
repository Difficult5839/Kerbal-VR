using System.IO;
using KSP.UI.Screens;
using UnityEngine;

namespace KerbalVR
{
	[KSPAddon(KSPAddon.Startup.MainMenu, true)]
	public class KerbalVRToolbar : MonoBehaviour
	{
		ApplicationLauncherButton m_toggleButton;
		Texture2D m_iconOn;
		Texture2D m_iconOff;
		bool m_lastRunningState;

		void Awake()
		{
			DontDestroyOnLoad(this);
			GameEvents.onGUIApplicationLauncherReady.Add(OnAppLauncherReady);
			GameEvents.onGUIApplicationLauncherDestroyed.Add(OnAppLauncherDestroyed);
		}

		void Start()
		{
			TryCreateButton();
		}

		void OnDestroy()
		{
			GameEvents.onGUIApplicationLauncherReady.Remove(OnAppLauncherReady);
			GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnAppLauncherDestroyed);

			RemoveButton();
		}

		void Update()
		{
			if (m_toggleButton != null && m_lastRunningState != Core.IsVrRunning)
			{
				SetButtonState(Core.IsVrRunning);
			}
		}

		void OnAppLauncherReady()
		{
			TryCreateButton();
		}

		void OnAppLauncherDestroyed()
		{
			RemoveButton();
		}

		void TryCreateButton()
		{
			if (m_toggleButton != null || !ApplicationLauncher.Ready)
			{
				return;
			}

			LoadIcons();

			Texture2D defaultIcon = m_iconOff ? m_iconOff : m_iconOn;
			if (!defaultIcon)
			{
				Utils.LogWarning("KerbalVR toolbar icon not found");
				return;
			}

			m_toggleButton = ApplicationLauncher.Instance.AddModApplication(
				OnToggleEnabled,
				OnToggleDisabled,
				null,
				null,
				null,
				null,
				ApplicationLauncher.AppScenes.FLIGHT,
				defaultIcon);

			SetButtonState(Core.IsVrRunning);
		}

		void RemoveButton()
		{
			if (m_toggleButton == null)
			{
				return;
			}

			if (ApplicationLauncher.Instance != null)
			{
				ApplicationLauncher.Instance.RemoveModApplication(m_toggleButton);
			}

			m_toggleButton = null;
		}

		void LoadIcons()
		{
			if (!m_iconOn)
			{
				string onPath = Path.Combine(Globals.KERBALVR_TEXTURES_DIR, "app_button_logo").Replace('\\', '/');
				m_iconOn = GameDatabase.Instance.GetTexture(onPath, false);
			}

			if (!m_iconOff)
			{
				string offPath = Path.Combine(Globals.KERBALVR_TEXTURES_DIR, "app_button_logo_alt").Replace('\\', '/');
				m_iconOff = GameDatabase.Instance.GetTexture(offPath, false);
			}
		}

		void SetButtonState(bool running)
		{
			if (m_toggleButton == null)
			{
				return;
			}

			Texture2D icon = running ? (m_iconOn ? m_iconOn : m_iconOff) : (m_iconOff ? m_iconOff : m_iconOn);
			if (icon)
			{
				m_toggleButton.SetTexture(icon);
			}

			if (running)
			{
				m_toggleButton.SetTrue(false);
			}
			else
			{
				m_toggleButton.SetFalse(false);
			}

			m_lastRunningState = running;
		}

		void OnToggleEnabled()
		{
			FirstPersonKerbalAddon.SetVrRunningState(true, "toolbar");
			SetButtonState(Core.IsVrRunning);
		}

		void OnToggleDisabled()
		{
			FirstPersonKerbalAddon.SetVrRunningState(false, "toolbar");
			SetButtonState(Core.IsVrRunning);
		}
	}
}
