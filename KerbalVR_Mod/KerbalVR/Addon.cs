using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;
using Valve.VR;

namespace KerbalVR
{
	[KSPAddon(KSPAddon.Startup.Instantly, true)]
	public class FirstPersonKerbalAddon : MonoBehaviour
	{
		static KeyBinding m_vrToggle = new KeyBinding(KeyCode.V);
		static KeyCode m_vrToggleKey = KeyCode.V;
		static bool m_toggleRequiresModifier = true;
		static float m_nextModifierHintTime = 0f;
		static public Vector3 kerbalEyePosition = new Vector3(0, 0.7f, 0);
		const float KeyboardYawRotationSpeedDegPerSecond = 60f;
		const float ViewHeightOffsetMin = -2f;
		const float ViewHeightOffsetMax = 2f;
		static float m_viewHeightOffset = 0f;
		static float m_viewYawOffsetDegrees = 0f;
		static bool m_keyboardViewRotationEnabled = false;

		internal static float ViewHeightOffset
		{
			get => m_viewHeightOffset;
			set
			{
				m_viewHeightOffset = Mathf.Clamp(value, ViewHeightOffsetMin, ViewHeightOffsetMax);
				ApplyCurrentKerbalEyePosition();
			}
		}

		internal static bool KeyboardViewRotationEnabled
		{
			get => m_keyboardViewRotationEnabled;
			set => m_keyboardViewRotationEnabled = value;
		}

		internal static float ViewYawOffsetDegrees => m_viewYawOffsetDegrees;

		internal static void ResetViewYawOffset()
		{
			m_viewYawOffsetDegrees = 0f;
		}

		internal static Vector3 GetConfiguredKerbalEyePosition()
		{
			return kerbalEyePosition + Vector3.up * m_viewHeightOffset;
		}

		internal static void ApplyCurrentKerbalEyePosition()
		{
			var cameraManager = CameraManager.Instance;
			if (!Core.IsVrRunning || !Scene.IsInIVA() || cameraManager?.IVACameraActiveKerbal == null)
			{
				return;
			}

			cameraManager.IVACameraActiveKerbal.eyeTransform.localPosition = GetConfiguredKerbalEyePosition();
		}

		public void Awake()
		{
			Utils.Log("Addon Awake");
			DontDestroyOnLoad(this);

			if (XRSettings.enabled)
			{
				Core.InitSteamVRInput();
				SteamVR.Initialize();
				HardwareUtils.Init();

				// for whatever reason, enabling VR mode during loading makes it super slow (vsync maybe?)
				XRSettings.enabled = false;

				ApplyPatches();
			}
			else
			{
				Utils.Log("VR is not enabled");
			}

			GameEvents.onLevelWasLoaded.Add(OnLevelWasLoaded);
			GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequested);
		}

		public static void ModuleManagerPostLoad()
		{
			Utils.Log("ModuleManagerPostLoad");

			KeyCode toggleKey = GameSettings.CAMERA_NEXT.primary.code;
			bool toggleRequiresModifier = true;
			float renderScale = RenderScaleController.ManualScale;
			bool dynamicRenderScale = RenderScaleController.DynamicEnabled;
			float dynamicRenderScaleMin = RenderScaleController.MinScale;
			float dynamicRenderScaleMax = RenderScaleController.MaxScale;
			float dynamicRenderScaleTargetFps = RenderScaleController.TargetFps;
			float dynamicRenderScaleStepDown = 0.08f;
			float dynamicRenderScaleStepUp = 0.04f;
			float viewHeightOffset = ViewHeightOffset;
			bool keyboardViewRotationEnabled = KeyboardViewRotationEnabled;

			var settingsNode = GameDatabase.Instance.GetConfigs("KerbalVRConfig").FirstOrDefault();

			if (settingsNode != null)
			{
				settingsNode.config.TryGetValue(nameof(kerbalEyePosition), ref kerbalEyePosition);
				settingsNode.config.TryGetEnum<KeyCode>("toggleKey", ref toggleKey, toggleKey);

				string toggleRequiresModifierString = null;
				if (settingsNode.config.TryGetValue("toggleRequiresModifier", ref toggleRequiresModifierString))
				{
					bool.TryParse(toggleRequiresModifierString, out toggleRequiresModifier);
				}

				TryGetFloat(settingsNode.config, "renderScale", ref renderScale);
				TryGetBool(settingsNode.config, "dynamicRenderScale", ref dynamicRenderScale);
				TryGetFloat(settingsNode.config, "dynamicRenderScaleMin", ref dynamicRenderScaleMin);
				TryGetFloat(settingsNode.config, "dynamicRenderScaleMax", ref dynamicRenderScaleMax);
				TryGetFloat(settingsNode.config, "dynamicRenderScaleTargetFps", ref dynamicRenderScaleTargetFps);
				TryGetFloat(settingsNode.config, "dynamicRenderScaleStepDown", ref dynamicRenderScaleStepDown);
				TryGetFloat(settingsNode.config, "dynamicRenderScaleStepUp", ref dynamicRenderScaleStepUp);
				TryGetFloat(settingsNode.config, "viewHeightOffset", ref viewHeightOffset);
				TryGetBool(settingsNode.config, "keyboardViewRotationEnabled", ref keyboardViewRotationEnabled);
			}

			m_vrToggleKey = toggleKey;
			m_toggleRequiresModifier = toggleRequiresModifier;
			m_vrToggle = new KeyBinding(toggleKey);
			ViewHeightOffset = viewHeightOffset;
			KeyboardViewRotationEnabled = keyboardViewRotationEnabled;
			ResetViewYawOffset();
			RenderScaleController.Configure(
				renderScale,
				dynamicRenderScale,
				dynamicRenderScaleMin,
				dynamicRenderScaleMax,
				dynamicRenderScaleTargetFps,
				dynamicRenderScaleStepDown,
				dynamicRenderScaleStepUp);

			string toggleDescription = m_toggleRequiresModifier ? $"{GetModifierKeyLabel()}+{m_vrToggleKey}" : $"{m_vrToggleKey}";
			Utils.Log($"VR toggle hotkey configured: {toggleDescription}");
			Utils.Log($"Dynamic render scale configured: enabled={RenderScaleController.DynamicEnabled}, baseScale={RenderScaleController.ManualScale:0.00}, min={RenderScaleController.MinScale:0.00}, max={RenderScaleController.MaxScale:0.00}, targetFps={(RenderScaleController.TargetFps <= 0f ? "auto" : RenderScaleController.TargetFps.ToString("0.0", CultureInfo.InvariantCulture))}");
			Utils.Log($"View adjustments configured: heightOffset={ViewHeightOffset:0.000}, keyboardViewRotationEnabled={KeyboardViewRotationEnabled}");
		}

		private static void ApplyPatches()
		{
			var harmony = new Harmony("KerbalVR");
			harmony.PatchAll(Assembly.GetExecutingAssembly());
			CameraFOVPatch.PatchAll(harmony);
		}

		public void LateUpdate()
		{
			if (IsToggleHotkeyPressed())
			{
				ToggleVrRunningState("hotkey");
			}
			else if (m_toggleRequiresModifier && m_vrToggle.GetKeyDown())
			{
				PostModifierHint();
			}

			UpdateKeyboardViewRotation();
			RenderScaleController.Update();
		}

		internal static void ToggleVrRunningState(string source)
		{
			SetVrRunningState(!Core.IsVrRunning, source);
		}

		internal static void SetVrRunningState(bool running, string source)
		{
			if (!Core.IsVrEnabled)
			{
				Utils.PostScreenMessage("VR is not enabled. Check KerbalVR installation.");
				return;
			}

			bool initialRunning = Core.IsVrRunning;
			Core.SetVrRunningDesired(running);

			if (Core.IsVrRunning != initialRunning)
			{
				Utils.PostScreenMessage(Core.IsVrRunning ? "VR enabled" : "VR disabled");
			}
			else if (running != Core.IsVrRunning)
			{
				Utils.PostScreenMessage("VR toggle requested, but no state change occurred.");
			}

			Utils.Log($"VR state request from {source}: requested={running}, initial={initialRunning}, current={Core.IsVrRunning}, scene={HighLogic.LoadedScene}");
		}

		static bool IsToggleHotkeyPressed()
		{
			if (!m_vrToggle.GetKeyDown())
			{
				return false;
			}

			if (!m_toggleRequiresModifier)
			{
				return true;
			}

			return IsModifierPressed();
		}

		static bool IsModifierPressed()
		{
			return GameSettings.MODIFIER_KEY.GetKey() || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
		}

		static void UpdateKeyboardViewRotation()
		{
			if (!m_keyboardViewRotationEnabled || !Core.IsVrRunning || !Scene.IsInIVA())
			{
				return;
			}

			float rotationInput = 0f;
			if (Input.GetKey(KeyCode.Q))
			{
				rotationInput -= 1f;
			}
			if (Input.GetKey(KeyCode.E))
			{
				rotationInput += 1f;
			}

			if (Mathf.Abs(rotationInput) < Mathf.Epsilon)
			{
				return;
			}

			m_viewYawOffsetDegrees += rotationInput * KeyboardYawRotationSpeedDegPerSecond * Time.unscaledDeltaTime;
			m_viewYawOffsetDegrees = Mathf.Repeat(m_viewYawOffsetDegrees + 180f, 360f) - 180f;
		}

		static void PostModifierHint()
		{
			if (Time.unscaledTime < m_nextModifierHintTime)
			{
				return;
			}

			m_nextModifierHintTime = Time.unscaledTime + 2f;
			Utils.PostScreenMessage($"Press {GetModifierKeyLabel()}+{m_vrToggleKey} to toggle VR");
		}

		static string GetModifierKeyLabel()
		{
			try
			{
				return GameSettings.MODIFIER_KEY.primary.code.ToString();
			}
			catch
			{
				return "Modifier";
			}
		}

		static void TryGetBool(ConfigNode config, string key, ref bool value)
		{
			string raw = null;
			if (!config.TryGetValue(key, ref raw))
			{
				return;
			}

			if (bool.TryParse(raw, out bool parsed))
			{
				value = parsed;
			}
		}

		static void TryGetFloat(ConfigNode config, string key, ref float value)
		{
			string raw = null;
			if (!config.TryGetValue(key, ref raw))
			{
				return;
			}

			if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
			{
				value = parsed;
			}
		}

		private void OnGameSceneLoadRequested(GameScenes data)
		{
			if (InteractionSystem.Instance != null)
			{
				InteractionSystem.Instance.transform.SetParent(null, false);
				GameObject.DontDestroyOnLoad(InteractionSystem.Instance);
			}

		}

		public void OnDestroy()
		{
			GameEvents.onLevelWasLoaded.Remove(OnLevelWasLoaded);
			PSystemManager.Instance.OnPSystemReady.Remove(OnPSystemReady);
		}

		public void OnLevelWasLoaded(GameScenes gameScene)
		{
			Utils.Log($"OnLevelWasLoaded: {gameScene}");

			if (gameScene == GameScenes.PSYSTEM)
			{
				KerbalVR.Core.InitSystems();
			}

			if (KerbalVR.Core.IsVrEnabled)
			{
				if (gameScene == GameScenes.PSYSTEM)
				{
					Valve.VR.SteamVR_Settings.instance.trackingSpace = Valve.VR.ETrackingUniverseOrigin.TrackingUniverseSeated;
					Valve.VR.SteamVR_Settings.instance.lockPhysicsUpdateRateToRenderFrequency = false;

					PSystemManager.Instance.OnPSystemReady.Add(OnPSystemReady);
				}

				KerbalVR.Core.SetVrRunningDesired(Core.IsVrRunning && Scene.SceneSupportsVR(gameScene));
			}
		}
		private void OnPSystemReady()
		{
			Utils.Log("OnPSystemReady");

			GameObject.Find("UIMainCamera").GetComponent<Camera>().stereoTargetEye = StereoTargetEyeMask.None;
			GameObject.Find("UIVectorCamera").GetComponent<Camera>().stereoTargetEye = StereoTargetEyeMask.None;

			Core.InitHeadsetState();
		}

		[HarmonyPatch(typeof(CameraManager), "Update")]
		class CameraManagerPatch
		{
			public static bool Prefix()
			{
				if (IsToggleHotkeyPressed())
				{
					return false;
				}
				return true;
			}
		}

		[HarmonyPatch(typeof(PSystemManager), nameof(PSystemManager.SetupScaledSpace))]
		class PSystemManagerPatch
		{
			public static void Postfix()
			{
				if (KerbalVR.Core.IsVrEnabled)
				{
					Camera scaledCamera = ScaledCamera.Instance.cam;
					Camera galaxyCamera = ScaledCamera.Instance.galaxyCamera;

					// fudge the scaled camera
					var scaledCameraAnchor = CameraUtils.CreateVRAnchor(scaledCamera);
					scaledCameraAnchor.transform.localScale = Vector3.one * ScaledSpace.InverseScaleFactor;
					var dummyListener = scaledCameraAnchor.AddComponent<AudioListener>(); // PlanetariumCamera.Awake requires an audiolistener
					var dummyCamera = scaledCameraAnchor.AddComponent<Camera>(); // PlanetariumCamera.Deactivate requires a camera object
					PlanetariumCamera._fetch = CameraUtils.MoveComponent<PlanetariumCamera>(scaledCamera.gameObject, scaledCameraAnchor);
					PSystemManager.Instance.scaledSpaceCamera = PlanetariumCamera.fetch;
					Component.DestroyImmediate(dummyListener);
					dummyCamera.enabled = false;

					PlanetariumCamera.camRef = scaledCamera;
					var scaledCameraDriver = CameraUtils.MoveComponent<ScaledCamera>(scaledCamera.gameObject, scaledCameraAnchor);

					// disable position tracking on the galaxy camera
					var galaxyCameraAnchor = CameraUtils.CreateVRAnchor(galaxyCamera);
					galaxyCameraAnchor.transform.localScale = Vector3.zero;
					var followRot = CameraUtils.MoveComponent<FollowRot>(galaxyCamera.gameObject, galaxyCameraAnchor);
					followRot.tgt = scaledCameraAnchor.transform;
				}
			}
		}
	}
	
}
