// Power Draw Overlay for Oxygen Not Included
// Copyright (c) 2026 Lior Rozin. MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PowerDrawOverlay
{
	/// <summary>
	/// Runs after the power overlay's own update. Wires get a color by how full their circuit is,
	/// and buildings that draw power get a color by how much they draw right now.
	/// </summary>
	[HarmonyPatch(typeof(OverlayModes.Power), nameof(OverlayModes.Power.Update))]
	internal static class Power_Update_Patch
	{
		private static readonly Dictionary<ushort, Color32?> wireColors = new Dictionary<ushort, Color32?>();
		private static readonly HashSet<KBatchedAnimController> tinted = new HashSet<KBatchedAnimController>();
		private static readonly HashSet<KBatchedAnimController> seen = new HashSet<KBatchedAnimController>();
		private static readonly List<KeyValuePair<KBatchedAnimController, Draw>> visible = new List<KeyValuePair<KBatchedAnimController, Draw>>();
		private static readonly List<KBatchedAnimController> stale = new List<KBatchedAnimController>();
		private static bool errorLogged;

		/// <summary>Largest current draw among the buildings on screen. Only used for buildings that are not on a circuit.</summary>
		internal static float MaxDraw = 1f;

		private static void Postfix(OverlayModes.Power __instance)
		{
			try
			{
				ColorWires(__instance);
				ColorConsumers();
			}
			catch (Exception e)
			{
				if (!errorLogged)
				{
					errorLogged = true;
					Debug.LogError("[PowerDrawOverlay] " + e);
				}
			}
		}

		private static void ColorWires(OverlayModes.Power overlay)
		{
			Traverse traverse = Traverse.Create(overlay);
			HybridListHashSet<SaveLoadRoot> targets = traverse.Field("layerTargets").GetValue<HybridListHashSet<SaveLoadRoot>>();
			HashSet<UtilityNetwork> connected = traverse.Field("connectedNetworks").GetValue<HashSet<UtilityNetwork>>();
			if (targets == null)
			{
				return;
			}

			CircuitManager circuits = Game.Instance.circuitManager;
			float pulse = (connected != null && connected.Count > 0) ? OverlayModes.ModeUtil.GetHighlightScale() : 1f;
			wireColors.Clear();

			foreach (SaveLoadRoot root in targets.AsList())
			{
				if (root == null || !root.TryGetComponent(out IBridgedNetworkItem networkItem))
				{
					continue;
				}
				KBatchedAnimController controller = root.GetComponent<KBatchedAnimController>();
				if (controller == null)
				{
					continue;
				}
				UtilityNetwork network = Game.Instance.electricalConduitSystem.GetNetworkForCell(networkItem.GetNetworkCell());
				if (network == null)
				{
					continue;
				}

				ushort id = (ushort)network.id;
				if (!wireColors.TryGetValue(id, out Color32? color))
				{
					float used = circuits.GetWattsUsedByCircuit(id);
					float max = circuits.GetMaxSafeWattageForCircuit(id);
					color = (used > 0f && max > 0f) ? Heat.Gradient(used / max) : (Color32?)null;
					wireColors.Add(id, color);
				}
				if (!color.HasValue)
				{
					// Idle or unknown circuit: keep the game's own color.
					continue;
				}

				Color32 final = color.Value;
				if (pulse != 1f && networkItem.IsConnectedToNetworks(connected))
				{
					final = Heat.Scale(final, pulse);
				}
				controller.TintColour = final;
			}
		}

		private static void ColorConsumers()
		{
			Grid.GetVisibleExtents(out Vector2I min, out Vector2I max);
			min.x = Mathf.Clamp(min.x, 0, Grid.WidthInCells - 1);
			min.y = Mathf.Clamp(min.y, 0, Grid.HeightInCells - 1);
			max.x = Mathf.Clamp(max.x, 0, Grid.WidthInCells - 1);
			max.y = Mathf.Clamp(max.y, 0, Grid.HeightInCells - 1);
			int world = ClusterManager.Instance.activeWorldId;

			visible.Clear();
			seen.Clear();
			float maxDraw = 0f;
			foreach (EnergyConsumer consumer in Components.EnergyConsumers.Items)
			{
				if (consumer == null)
				{
					continue;
				}
				Vector2I xy = Grid.PosToXY(consumer.transform.GetPosition());
				if (!(min <= xy && xy <= max) || Grid.WorldIdx[Grid.XYToCell(xy.x, xy.y)] != world)
				{
					continue;
				}
				KBatchedAnimController controller = consumer.GetComponent<KBatchedAnimController>();
				if (controller == null)
				{
					continue;
				}
				float used = Mathf.Max(0f, consumer.WattsUsed);
				maxDraw = Mathf.Max(maxDraw, used);
				seen.Add(controller);
				visible.Add(new KeyValuePair<KBatchedAnimController, Draw>(controller, new Draw(used, consumer.CircuitID)));
			}
			MaxDraw = Mathf.Max(maxDraw, 1f);

			int layer = LayerMask.NameToLayer("MaskedOverlay");
			foreach (KeyValuePair<KBatchedAnimController, Draw> entry in visible)
			{
				if (tinted.Add(entry.Key))
				{
					entry.Key.SetLayer(layer);
				}
				entry.Key.TintColour = ColorForDraw(entry.Value.Watts, entry.Value.Circuit);
			}

			stale.Clear();
			foreach (KBatchedAnimController controller in tinted)
			{
				if (controller == null || !seen.Contains(controller))
				{
					stale.Add(controller);
				}
			}
			foreach (KBatchedAnimController controller in stale)
			{
				tinted.Remove(controller);
				Reset(controller);
			}
		}

		private readonly struct Draw
		{
			public readonly float Watts;
			public readonly ushort Circuit;

			public Draw(float watts, ushort circuit)
			{
				Watts = watts;
				Circuit = circuit;
			}
		}

		/// <summary>
		/// Scales the draw against the safe wattage of the building's own circuit, so a 1000 W draw
		/// is mild on a 20000 W wire and red on a 1000 W one.
		/// </summary>
		internal static Color32 ColorForDraw(float used, ushort circuit)
		{
			if (used <= 0f)
			{
				return Heat.Idle;
			}
			float limit = 0f;
			if (circuit != ushort.MaxValue)
			{
				limit = Game.Instance.circuitManager.GetMaxSafeWattageForCircuit(circuit);
			}
			if (limit <= 0f)
			{
				limit = MaxDraw;
			}
			// Square root keeps small consumers visible next to a much larger limit.
			return Heat.Gradient(Mathf.Sqrt(used / limit));
		}

		internal static void ResetAll()
		{
			foreach (KBatchedAnimController controller in tinted)
			{
				Reset(controller);
			}
			tinted.Clear();
			MaxDraw = 1f;
		}

		private static void Reset(KBatchedAnimController controller)
		{
			if (controller == null)
			{
				return;
			}
			controller.SetLayer(0);
			controller.HighlightColour = Color.clear;
			controller.TintColour = Color.white;
			controller.SetLayer(controller.GetComponent<KPrefabID>().defaultLayer);
		}
	}

	[HarmonyPatch(typeof(OverlayModes.Power), nameof(OverlayModes.Power.Disable))]
	internal static class Power_Disable_Patch
	{
		private static void Postfix()
		{
			Power_Update_Patch.ResetAll();
		}
	}

	/// <summary>Colors the wattage numbers above buildings to match the building color.</summary>
	[HarmonyPatch(typeof(OverlayModes.Power), "UpdatePowerLabels")]
	internal static class Power_UpdatePowerLabels_Patch
	{
		private static bool errorLogged;

		private static void Postfix(OverlayModes.Power __instance)
		{
			try
			{
				IEnumerable entries = Traverse.Create(__instance).Field("updatePowerInfo").GetValue() as IEnumerable;
				if (entries == null)
				{
					return;
				}
				foreach (object entry in entries)
				{
					Traverse info = Traverse.Create(entry);
					IEnergyConsumer consumer = info.Field("consumer").GetValue<IEnergyConsumer>();
					if (consumer == null)
					{
						continue;
					}
					KMonoBehaviour owner = info.Field("item").GetValue<KMonoBehaviour>();
					if (owner == null)
					{
						continue;
					}
					BuildingEnabledButton enabledButton;
					if (owner.TryGetComponent(out enabledButton) && !enabledButton.IsEnabled)
					{
						continue;
					}
					LocText label = info.Field("powerLabel").GetValue<LocText>();
					LocText unit = info.Field("unitLabel").GetValue<LocText>();
					if (label == null || unit == null)
					{
						continue;
					}
					Color color = Power_Update_Patch.ColorForDraw(Mathf.Max(0f, consumer.WattsUsed), Game.Instance.circuitManager.GetCircuitID(consumer));
					label.color = color;
					unit.color = color;
				}
			}
			catch (Exception e)
			{
				if (!errorLogged)
				{
					errorLogged = true;
					Debug.LogError("[PowerDrawOverlay] " + e);
				}
			}
		}
	}
}
