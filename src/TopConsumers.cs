// Power Draw Overlay for Oxygen Not Included
// Copyright (c) 2026 Lior Rozin. MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;

namespace PowerDrawOverlay
{
	/// <summary>
	/// Adds a "biggest consumers" line to the hover card of every wire while the power overlay is on.
	/// </summary>
	internal static class TopConsumers
	{
		private const int MaxRows = 8;

		private static StatusItem item;

		public static StatusItem Item
		{
			get
			{
				if (item == null)
				{
					item = new StatusItem(
						"PowerDrawOverlayTopConsumers",
						"Biggest consumers on this circuit",
						"Power drawn right now, with the rated power in brackets. Buildings of the same type are added together.",
						"",
						StatusItem.IconType.Info,
						NotificationType.Neutral,
						false,
						OverlayModes.Power.ID);
					item.resolveStringCallback = Resolve;
					item.resolveTooltipCallback = (str, data) => str;
					item.showInHoverCardOnly = true;
				}
				return item;
			}
		}

		private class Group
		{
			public int count;
			public float used;
			public float rated;
		}

		private static string Resolve(string template, object data)
		{
			Wire wire = data as Wire;
			if (wire == null)
			{
				return template;
			}
			CircuitManager circuits = Game.Instance.circuitManager;
			ushort id = circuits.GetCircuitID(Grid.PosToCell(wire.transform.GetPosition()));
			if (id == ushort.MaxValue)
			{
				return template;
			}
			List<IEnergyConsumer> consumers = circuits.GetConsumersOnCircuit(id);
			if (consumers == null || consumers.Count == 0)
			{
				return template + "\nNothing is drawing power here.";
			}

			Dictionary<string, Group> groups = new Dictionary<string, Group>();
			foreach (IEnergyConsumer consumer in consumers)
			{
				if (consumer == null)
				{
					continue;
				}
				string name = consumer.Name;
				if (!groups.TryGetValue(name, out Group group))
				{
					group = new Group();
					groups.Add(name, group);
				}
				group.count++;
				group.used += consumer.WattsUsed;
				group.rated += consumer.WattsNeededWhenActive;
			}

			List<KeyValuePair<string, Group>> ordered = groups
				.OrderByDescending(pair => pair.Value.used)
				.ThenByDescending(pair => pair.Value.rated)
				.ToList();

			StringBuilder text = new StringBuilder(template);
			foreach (KeyValuePair<string, Group> pair in ordered.Take(MaxRows))
			{
				text.Append('\n').Append(pair.Key);
				if (pair.Value.count > 1)
				{
					text.Append(" x").Append(pair.Value.count);
				}
				text.Append(": ")
					.Append(GameUtil.GetFormattedWattage(pair.Value.used))
					.Append(" (")
					.Append(GameUtil.GetFormattedWattage(pair.Value.rated))
					.Append(')');
			}
			if (ordered.Count > MaxRows)
			{
				text.Append("\n+").Append(ordered.Count - MaxRows).Append(" more");
			}
			return text.ToString();
		}
	}

	[HarmonyPatch(typeof(Wire), "OnSpawn")]
	internal static class Wire_OnSpawn_Patch
	{
		private static void Postfix(Wire __instance)
		{
			KSelectable selectable = __instance.GetComponent<KSelectable>();
			if (selectable != null)
			{
				selectable.AddStatusItem(TopConsumers.Item, __instance);
			}
		}
	}
}
