using HarmonyLib;
using System;
using System.Collections.Generic;
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
			}

			m_vrToggleKey = toggleKey;
			m_toggleRequiresModifier = toggleRequiresModifier;
			m_vrToggle = new KeyBinding(toggleKey);

			string toggleDescription = m_toggleRequiresModifier ? $"{GetModifierKeyLabel()}+{m_vrToggleKey}" : $"{m_vrToggleKey}";
			Utils.Log($"VR toggle hotkey configured: {toggleDescription}");
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
