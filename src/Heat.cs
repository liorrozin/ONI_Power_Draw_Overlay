// Power Draw Overlay for Oxygen Not Included
// Copyright (c) 2026 Lior Rozin. MIT License.

using UnityEngine;

namespace PowerDrawOverlay
{
	internal static class Heat
	{
		/// <summary>Idle or switched-off buildings.</summary>
		public static readonly Color32 Idle = new Color32(120, 126, 140, 255);

		/// <summary>0 = cool blue, about 0.5 = green/yellow, 1 = red.</summary>
		public static Color32 Gradient(float t)
		{
			t = Mathf.Clamp01(t);
			float hue = Mathf.Lerp(0.60f, 0f, t);
			return Color.HSVToRGB(hue, 0.85f, 1f);
		}

		public static Color32 Scale(Color32 c, float factor)
		{
			return new Color32(
				(byte)Mathf.Clamp(c.r * factor, 0f, 255f),
				(byte)Mathf.Clamp(c.g * factor, 0f, 255f),
				(byte)Mathf.Clamp(c.b * factor, 0f, 255f),
				c.a);
		}
	}
}
