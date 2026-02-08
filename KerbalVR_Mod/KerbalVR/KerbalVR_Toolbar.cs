using System.Globalization;
using System.IO;
using KSP.UI.Screens;
using UnityEngine;

namespace KerbalVR
{
	[KSPAddon(KSPAddon.Startup.MainMenu, true)]
	public class KerbalVRToolbar : MonoBehaviour
	{
		const float WindowWidth = 320f;
		const float WindowHeight = 430f;
		const float SliderScaleMin = 0.5f;
		const float SliderScaleMax = 1.5f;

		ApplicationLauncherButton m_toggleButton;
		Texture2D m_iconOn;
		Texture2D m_iconOff;
		bool m_lastRunningState;
		bool m_showSettingsWindow;
		bool m_windowPositionInitialized;
		Rect m_settingsWindowRect = new Rect(0f, 0f, WindowWidth, WindowHeight);
		readonly int m_settingsWindowId = typeof(KerbalVRToolbar).GetHashCode();
		string m_heightOffsetInput = "0";
		string m_heightOffsetError;

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
			if (m_showSettingsWindow && HighLogic.LoadedScene != GameScenes.FLIGHT)
			{
				m_showSettingsWindow = false;
				SyncButtonSelection();
			}

			if (m_toggleButton != null && m_lastRunningState != Core.IsVrRunning)
			{
				SetButtonIcon(Core.IsVrRunning);
			}
		}

		void OnGUI()
		{
			if (!m_showSettingsWindow || HighLogic.LoadedScene != GameScenes.FLIGHT)
			{
				return;
			}

			EnsureWindowRect();
			m_settingsWindowRect = GUILayout.Window(m_settingsWindowId, m_settingsWindowRect, DrawSettingsWindow, "KerbalVR Settings");
			ClampWindowToScreen();
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

			SetButtonIcon(Core.IsVrRunning);
			SyncButtonSelection();
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

		void SetButtonIcon(bool running)
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

			m_lastRunningState = running;
		}

		void SyncButtonSelection()
		{
			if (m_toggleButton == null)
			{
				return;
			}

			if (m_showSettingsWindow)
			{
				m_toggleButton.SetTrue(false);
			}
			else
			{
				m_toggleButton.SetFalse(false);
			}
		}

		void OnToggleEnabled()
		{
			OpenSettingsWindow();
		}

		void OnToggleDisabled()
		{
			m_showSettingsWindow = false;
			SyncButtonSelection();
		}

		void OpenSettingsWindow()
		{
			m_showSettingsWindow = true;
			SyncHeightOffsetInput();
			EnsureWindowRect();
			SyncButtonSelection();
		}

		void EnsureWindowRect()
		{
			if (m_windowPositionInitialized)
			{
				return;
			}

			m_settingsWindowRect = new Rect(Screen.width - WindowWidth - 25f, 90f, WindowWidth, WindowHeight);
			ClampWindowToScreen();
			m_windowPositionInitialized = true;
		}

		void ClampWindowToScreen()
		{
			float maxX = Mathf.Max(0f, Screen.width - m_settingsWindowRect.width);
			float maxY = Mathf.Max(0f, Screen.height - m_settingsWindowRect.height);

			m_settingsWindowRect.x = Mathf.Clamp(m_settingsWindowRect.x, 0f, maxX);
			m_settingsWindowRect.y = Mathf.Clamp(m_settingsWindowRect.y, 0f, maxY);
		}

		void DrawSettingsWindow(int windowId)
		{
			bool dynamicEnabled = RenderScaleController.DynamicEnabled;
			bool dynamicEnabledNew = GUILayout.Toggle(dynamicEnabled, "Dynamic scale (auto adjust for FPS)");
			if (dynamicEnabledNew != dynamicEnabled)
			{
				RenderScaleController.DynamicEnabled = dynamicEnabledNew;
			}

			DrawScaleSlider("Base scale", RenderScaleController.ManualScale, SliderScaleMin, SliderScaleMax, value => RenderScaleController.ManualScale = value);

			if (RenderScaleController.DynamicEnabled)
			{
				float minScaleLimit = Mathf.Max(SliderScaleMin, RenderScaleController.MaxScale - 0.01f);
				DrawScaleSlider("Min scale", RenderScaleController.MinScale, SliderScaleMin, minScaleLimit, value => RenderScaleController.MinScale = value);

				float maxScaleLimit = Mathf.Min(SliderScaleMax, RenderScaleController.MinScale + 0.01f);
				DrawScaleSlider("Max scale", RenderScaleController.MaxScale, maxScaleLimit, SliderScaleMax, value => RenderScaleController.MaxScale = value);

				bool autoTargetFps = RenderScaleController.TargetFps <= 0f;
				bool autoTargetFpsNew = GUILayout.Toggle(autoTargetFps, $"Auto target FPS ({RenderScaleController.EffectiveTargetFps:0.0})");
				if (autoTargetFpsNew != autoTargetFps)
				{
					RenderScaleController.TargetFps = autoTargetFpsNew ? 0f : RenderScaleController.EffectiveTargetFps;
				}

				if (!autoTargetFpsNew)
				{
					float targetFps = RenderScaleController.TargetFps;
					GUILayout.Label($"Target FPS: {targetFps:0.0}");
					float targetFpsNew = GUILayout.HorizontalSlider(targetFps, 45f, 120f);
					if (Mathf.Abs(targetFpsNew - targetFps) > 0.01f)
					{
						RenderScaleController.TargetFps = targetFpsNew;
					}
				}
			}

			GUILayout.Space(8f);
			GUILayout.Label("View Adjustment");

			GUILayout.BeginHorizontal();
			GUILayout.Label("Height offset (m):", GUILayout.Width(108f));
			string heightOffsetInputNew = GUILayout.TextField(m_heightOffsetInput, GUILayout.Width(68f));
			if (heightOffsetInputNew != m_heightOffsetInput)
			{
				m_heightOffsetInput = heightOffsetInputNew;
				m_heightOffsetError = null;
			}

			if (GUILayout.Button("Apply", GUILayout.Width(60f)))
			{
				ApplyHeightOffsetInput();
			}
			if (GUILayout.Button("Reset", GUILayout.Width(60f)))
			{
				FirstPersonKerbalAddon.ViewHeightOffset = 0f;
				SyncHeightOffsetInput();
			}
			GUILayout.EndHorizontal();

			GUILayout.Label($"Current height offset: {FirstPersonKerbalAddon.ViewHeightOffset:+0.000;-0.000;0.000} m");
			if (!string.IsNullOrEmpty(m_heightOffsetError))
			{
				GUILayout.Label(m_heightOffsetError);
			}

			bool keyboardRotateEnabled = FirstPersonKerbalAddon.KeyboardViewRotationEnabled;
			bool keyboardRotateEnabledNew = GUILayout.Toggle(keyboardRotateEnabled, "Enable Q/E view rotation");
			if (keyboardRotateEnabledNew != keyboardRotateEnabled)
			{
				FirstPersonKerbalAddon.KeyboardViewRotationEnabled = keyboardRotateEnabledNew;
			}

			GUILayout.Label("Q = rotate left, E = rotate right");
			GUILayout.Label($"Current yaw offset: {FirstPersonKerbalAddon.ViewYawOffsetDegrees:+0.0;-0.0;0.0}\u00b0");
			if (GUILayout.Button("Reset View Rotation"))
			{
				FirstPersonKerbalAddon.ResetViewYawOffset();
			}

			GUILayout.Space(6f);
			GUILayout.Label($"Current scale: {RenderScaleController.CurrentRenderScale:0.00}x");
			GUILayout.Label($"Smoothed FPS: {RenderScaleController.SmoothedFps:0.0}");

			GUILayout.BeginHorizontal();
			if (GUILayout.Button(Core.IsVrRunning ? "Disable VR" : "Enable VR"))
			{
				FirstPersonKerbalAddon.SetVrRunningState(!Core.IsVrRunning, "toolbar-window");
				SetButtonIcon(Core.IsVrRunning);
			}

			if (GUILayout.Button("Close"))
			{
				m_showSettingsWindow = false;
				SyncButtonSelection();
			}
			GUILayout.EndHorizontal();

			GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
		}

		static void DrawScaleSlider(string label, float currentValue, float minValue, float maxValue, System.Action<float> setter)
		{
			GUILayout.Label($"{label}: {currentValue:0.00}x");
			float newValue = GUILayout.HorizontalSlider(currentValue, minValue, maxValue);
			if (Mathf.Abs(newValue - currentValue) > 0.001f)
			{
				setter(newValue);
			}
		}

		void ApplyHeightOffsetInput()
		{
			if (!float.TryParse(m_heightOffsetInput, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
			{
				m_heightOffsetError = "Invalid number (use e.g. 0.05).";
				return;
			}

			FirstPersonKerbalAddon.ViewHeightOffset = parsed;
			SyncHeightOffsetInput();
		}

		void SyncHeightOffsetInput()
		{
			m_heightOffsetInput = FirstPersonKerbalAddon.ViewHeightOffset.ToString("0.###", CultureInfo.InvariantCulture);
			m_heightOffsetError = null;
		}
	}
}
