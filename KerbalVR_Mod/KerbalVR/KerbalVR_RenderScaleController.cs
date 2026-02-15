using UnityEngine;
using Valve.VR;

namespace KerbalVR
{
	public static class RenderScaleController
	{
		const float AbsoluteMinScale = 0.5f;
		const float AbsoluteMaxScale = 1.5f;
		const float AbsoluteSceneScaleMin = 0.1f;
		const float AbsoluteSceneScaleMax = 4.0f;
		const float MinTargetFps = 30f;
		const float MaxTargetFps = 144f;
		const float DefaultTargetFps = 60f;
		const float DefaultNonVrScale = 1f;
		const float FpsHysteresis = 2f;
		const float FpsSmoothingFactor = 0.1f;
		const float AdjustIntervalSeconds = 0.35f;

		static bool m_initialized;
		static bool m_dynamicEnabled = true;
		static float m_manualScale = 1f;
		static float m_minScale = 0.7f;
		static float m_maxScale = 1.2f;
		static float m_targetFps = DefaultTargetFps;
		static float m_stepDown = 0.08f;
		static float m_stepUp = 0.04f;
		static float m_currentScale = 1f;
		static float m_lastAppliedScale = -1f;
		static float m_smoothedFrameTime = 1f / DefaultTargetFps;
		static float m_smoothedFps = DefaultTargetFps;
		static float m_adjustTimer;
		static bool m_adaptiveUpdatesActive;
		static float m_nonVrScale = DefaultNonVrScale;
		static bool m_hasNonVrScaleSnapshot;
		static bool m_nonVrScaleSampledFromAdaptive;

		public static bool DynamicEnabled
		{
			get => m_dynamicEnabled;
			set
			{
				m_dynamicEnabled = value;
				if (!m_dynamicEnabled && Core.IsVrRunning)
				{
					ApplyRenderScale(m_manualScale);
				}
			}
		}

		public static float ManualScale
		{
			get => m_manualScale;
			set => m_manualScale = Mathf.Clamp(value, m_minScale, m_maxScale);
		}

		public static float MinScale
		{
			get => m_minScale;
			set
			{
				m_minScale = Mathf.Clamp(value, AbsoluteMinScale, AbsoluteMaxScale);
				if (m_maxScale < m_minScale)
				{
					m_maxScale = m_minScale;
				}

				if (m_manualScale < m_minScale)
				{
					m_manualScale = m_minScale;
				}
			}
		}

		public static float MaxScale
		{
			get => m_maxScale;
			set
			{
				m_maxScale = Mathf.Clamp(value, AbsoluteMinScale, AbsoluteMaxScale);
				if (m_minScale > m_maxScale)
				{
					m_minScale = m_maxScale;
				}

				if (m_manualScale > m_maxScale)
				{
					m_manualScale = m_maxScale;
				}
			}
		}

		public static float TargetFps
		{
			get => m_targetFps;
			set => m_targetFps = value <= 0f ? 0f : Mathf.Clamp(value, MinTargetFps, MaxTargetFps);
		}

		public static float CurrentRenderScale => m_currentScale;
		public static float SmoothedFps => m_smoothedFps;
		public static float EffectiveTargetFps => ResolveTargetFps();

		public static void Configure(
			float manualScale,
			bool dynamicEnabled,
			float minScale,
			float maxScale,
			float targetFps,
			float stepDown,
			float stepUp)
		{
			float clampedMin = Mathf.Clamp(minScale, AbsoluteMinScale, AbsoluteMaxScale);
			float clampedMax = Mathf.Clamp(maxScale, AbsoluteMinScale, AbsoluteMaxScale);
			if (clampedMax < clampedMin)
			{
				float temp = clampedMin;
				clampedMin = clampedMax;
				clampedMax = temp;
			}

			m_minScale = clampedMin;
			m_maxScale = clampedMax;
			m_manualScale = Mathf.Clamp(manualScale, m_minScale, m_maxScale);
			m_dynamicEnabled = dynamicEnabled;
			m_targetFps = targetFps <= 0f ? 0f : Mathf.Clamp(targetFps, MinTargetFps, MaxTargetFps);
			m_stepDown = Mathf.Clamp(stepDown, 0.005f, 0.25f);
			m_stepUp = Mathf.Clamp(stepUp, 0.005f, 0.25f);
			m_adaptiveUpdatesActive = Core.IsVrRunning;
			if (!Core.IsVrRunning)
			{
				CaptureNonVrScaleSnapshot(SteamVR_Camera.sceneResolutionScale, sampledFromActiveVrAdaptive: false);
			}
		}

		public static void OnVrRunningChanged(bool running)
		{
			m_adjustTimer = 0f;

			if (!running)
			{
				m_initialized = false;
				m_adaptiveUpdatesActive = false;
				ApplySceneResolutionScale(ResolveNonVrRestoreScale());
				return;
			}

			// Capture desktop baseline before VR writes its own scale for this session.
			CaptureNonVrScaleSnapshot(SteamVR_Camera.sceneResolutionScale, sampledFromActiveVrAdaptive: m_adaptiveUpdatesActive && m_dynamicEnabled);

			m_initialized = false;
			EnsureInitialized();

			ApplyRenderScale(m_manualScale);
			m_adaptiveUpdatesActive = true;
		}

		public static void Update()
		{
			if (!Core.IsVrEnabled || !Core.IsVrRunning)
			{
				m_adjustTimer = 0f;
				return;
			}
			if (!m_adaptiveUpdatesActive)
			{
				return;
			}

			EnsureInitialized();

			float deltaTime = Time.unscaledDeltaTime;
			if (deltaTime <= 0f)
			{
				return;
			}

			m_smoothedFrameTime = Mathf.Lerp(m_smoothedFrameTime, deltaTime, FpsSmoothingFactor);
			m_smoothedFps = 1f / Mathf.Max(0.0001f, m_smoothedFrameTime);

			if (!m_dynamicEnabled)
			{
				ApplyRenderScale(m_manualScale);
				return;
			}

			m_adjustTimer += deltaTime;
			if (m_adjustTimer < AdjustIntervalSeconds)
			{
				return;
			}
			m_adjustTimer = 0f;

			float targetFps = ResolveTargetFps();
			float desiredScale = m_currentScale;

			if (m_smoothedFps < targetFps - FpsHysteresis)
			{
				desiredScale -= m_stepDown;
			}
			else if (m_smoothedFps > targetFps + FpsHysteresis)
			{
				desiredScale += m_stepUp;
			}
			else
			{
				desiredScale = Mathf.MoveTowards(m_currentScale, m_manualScale, m_stepUp * 0.5f);
			}

			ApplyRenderScale(Mathf.Clamp(desiredScale, m_minScale, m_maxScale));
		}

		static void EnsureInitialized()
		{
			if (m_initialized)
			{
				return;
			}

			float targetFps = ResolveTargetFps();
			m_smoothedFrameTime = 1f / Mathf.Max(1f, targetFps);
			m_smoothedFps = targetFps;

			float configuredScale = GetValidSceneScale(SteamVR_Camera.sceneResolutionScale, m_manualScale);

			m_currentScale = Mathf.Clamp(configuredScale, m_minScale, m_maxScale);
			m_initialized = true;
		}

		static void ApplyRenderScale(float scale)
		{
			ApplySceneResolutionScale(Mathf.Clamp(scale, m_minScale, m_maxScale));
		}

		static void ApplySceneResolutionScale(float scale)
		{
			float clampedScale = Mathf.Clamp(scale, AbsoluteSceneScaleMin, AbsoluteSceneScaleMax);
			if (Mathf.Abs(clampedScale - m_lastAppliedScale) < 0.001f)
			{
				return;
			}

			SteamVR_Camera.sceneResolutionScale = clampedScale;
			m_lastAppliedScale = clampedScale;
			m_currentScale = clampedScale;
		}

		static float GetValidSceneScale(float scale, float fallback)
		{
			if (scale > 0f && !float.IsNaN(scale) && !float.IsInfinity(scale))
			{
				return Mathf.Clamp(scale, AbsoluteSceneScaleMin, AbsoluteSceneScaleMax);
			}

			return Mathf.Clamp(fallback, AbsoluteSceneScaleMin, AbsoluteSceneScaleMax);
		}

		static void CaptureNonVrScaleSnapshot(float sampledScale, bool sampledFromActiveVrAdaptive)
		{
			m_nonVrScale = GetValidSceneScale(sampledScale, DefaultNonVrScale);
			m_hasNonVrScaleSnapshot = true;
			m_nonVrScaleSampledFromAdaptive = sampledFromActiveVrAdaptive;
		}

		static float ResolveNonVrRestoreScale()
		{
			if (!m_hasNonVrScaleSnapshot || m_nonVrScaleSampledFromAdaptive)
			{
				return DefaultNonVrScale;
			}

			return GetValidSceneScale(m_nonVrScale, DefaultNonVrScale);
		}

		static float ResolveTargetFps()
		{
			if (m_targetFps > 0f)
			{
				return Mathf.Clamp(m_targetFps, MinTargetFps, MaxTargetFps);
			}

			try
			{
				if (SteamVR.instance != null)
				{
					float headsetRefresh = SteamVR.instance.hmd_DisplayFrequency;
					if (headsetRefresh > 0f)
					{
						return Mathf.Clamp(headsetRefresh, MinTargetFps, MaxTargetFps);
					}
				}
			}
			catch
			{
				// Ignore and use fallback target.
			}

			return DefaultTargetFps;
		}
	}
}
