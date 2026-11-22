// Power Draw Overlay for Oxygen Not Included
// Copyright (c) 2026 Lior Rozin. MIT License.

using HarmonyLib;
using KMod;

namespace PowerDrawOverlay
{
	public class Mod : UserMod2
	{
		public override void OnLoad(Harmony harmony)
		{
			// Applies every [HarmonyPatch] class in this assembly.
			base.OnLoad(harmony);
			Debug.Log("[PowerDrawOverlay] loaded");
		}
	}

	internal static class Debug
	{
		public static void Log(string message)
		{
			UnityEngine.Debug.Log(message);
		}

		public static void LogError(string message)
		{
			UnityEngine.Debug.LogError(message);
		}
	}
}
