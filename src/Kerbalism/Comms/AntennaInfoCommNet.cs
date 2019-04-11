﻿using System.Collections.Generic;
using System;
using KSP.Localization;
using CommNet;

namespace KERBALISM
{
	public sealed class AntennaInfoCommNet: AntennaInfo
	{
		public AntennaInfoCommNet(Vessel v, bool powered, bool storm)
		{
			List<ModuleDataTransmitter> transmitters;

			// if vessel is loaded
			if (v.loaded)
			{
				// find transmitters
				transmitters = v.FindPartModulesImplementing<ModuleDataTransmitter>();

				if (transmitters != null)
				{
					foreach (ModuleDataTransmitter t in transmitters)
					{
						// Disable all stock buttons
						t.Events["TransmitIncompleteToggle"].active = false;
						t.Events["StartTransmission"].active = false;
						t.Events["StopTransmission"].active = false;
						t.Actions["StartTransmissionAction"].active = false;

						if (t.antennaType == AntennaType.INTERNAL) // do not include internal data rate, ec cost only
							ec += t.DataResourceCost * t.DataRate;
						else
						{
							// do we have an animation
							ModuleDeployableAntenna animation = t.part.FindModuleImplementing<ModuleDeployableAntenna>();
							ModuleAnimateGeneric animationGeneric = t.part.FindModuleImplementing<ModuleAnimateGeneric>();
							if (animation != null)
							{
								// only include data rate and ec cost if transmitter is extended
								if (animation.deployState == ModuleDeployablePart.DeployState.EXTENDED)
								{
									rate += t.DataRate;
									ec += t.DataResourceCost * t.DataRate;
								}
							}
							else if (animationGeneric != null)
							{
								// only include data rate and ec cost if transmitter is extended
								if (animationGeneric.animSpeed > 0)
								{
									rate += t.DataRate;
									ec += t.DataResourceCost * t.DataRate;
								}
							}
							// no animation
							else
							{
								rate += t.DataRate;
								ec += t.DataResourceCost * t.DataRate;
							}
						}
					}
				}
			}
			// if vessel is not loaded
			else
			{
				// find proto transmitters
				foreach (ProtoPartSnapshot p in v.protoVessel.protoPartSnapshots)
				{
					// get part prefab (required for module properties)
					Part part_prefab = PartLoader.getPartInfoByName(p.partName).partPrefab;

					transmitters = part_prefab.FindModulesImplementing<ModuleDataTransmitter>();

					if (transmitters != null)
					{
						foreach (ModuleDataTransmitter t in transmitters)
						{
							if (t.antennaType == AntennaType.INTERNAL) // do not include internal data rate, ec cost only
								ec += t.DataResourceCost * t.DataRate;
							else
							{
								// do we have an animation
								ProtoPartModuleSnapshot m = p.FindModule("ModuleDeployableAntenna") ?? p.FindModule("ModuleAnimateGeneric");
								if (m != null)
								{
									// only include data rate and ec cost if transmitter is extended
									string deployState = Lib.Proto.GetString(m, "deployState");
									float animSpeed = Lib.Proto.GetFloat(m, "animSpeed");
									if (deployState == "EXTENDED" || animSpeed > 0)
									{
										rate += t.DataRate;
										ec += t.DataResourceCost * t.DataRate;
									}
								}
								// no animation
								else
								{
									rate += t.DataRate;
									ec += t.DataResourceCost * t.DataRate;
								}
							}
						}
					}
				}
			}

			Init(v, powered, storm);
		}

		private void Init(Vessel v, bool powered, bool storm)
		{
			if(!powered || v.connection == null)
			{
				linked = false;
				status = LinkStatus.no_link;
				return;
			}

			// force CommNet update of unloaded vessels
			if (!v.loaded)
				Lib.ReflectionValue(v.connection, "unloadedDoOnce", true);

			// are we connected to DSN
			if (v.connection.IsConnected)
			{
				linked = true;
				status = v.connection.ControlPath.First.hopType == CommNet.HopType.Home ? LinkStatus.direct_link : LinkStatus.indirect_link;
				strength = v.connection.SignalStrength;
				rate *= strength;
				target_name = Lib.Ellipsis(Localizer.Format(v.connection.ControlPath.First.end.displayName).Replace("Kerbin", "DSN"), 20);

				if (status != LinkStatus.direct_link)
				{
					Vessel firstHop = Lib.CommNodeToVessel(v.Connection.ControlPath.First.end);
					// Get rate from the firstHop, each Hop will do the same logic, then we will have the min rate for whole path
					rate = Math.Min(Cache.VesselInfo(FlightGlobals.FindVessel(firstHop.id)).connection.rate, rate);
				}
			}
			// is loss of connection due to plasma blackout
			else if (Lib.ReflectionValue<bool>(v.connection, "inPlasma"))  // calling InPlasma causes a StackOverflow :(
			{
				status = LinkStatus.plasma;
			}

			control_path = new List<string[]>();
			foreach (CommLink link in v.connection.ControlPath)
			{
				double antennaPower = link.end.isHome ? link.start.antennaTransmit.power + link.start.antennaRelay.power : link.start.antennaTransmit.power;
				double signalStrength = 1 - ((link.start.position - link.end.position).magnitude / Math.Sqrt(antennaPower * link.end.antennaRelay.power));
				signalStrength = (3 - (2 * signalStrength)) * Math.Pow(signalStrength, 2);

				string name = Lib.Ellipsis(Localizer.Format(link.end.name).Replace("Kerbin", "DSN"), 35);
				string value = Lib.HumanReadablePerc(Math.Ceiling(signalStrength * 10000) / 10000, "F2");
				string tooltip = "Distance: " + Lib.HumanReadableRange((link.start.position - link.end.position).magnitude) +
					"\nMax Distance: " + Lib.HumanReadableRange(Math.Sqrt((link.start.antennaTransmit.power + link.start.antennaRelay.power) * link.end.antennaRelay.power));
				control_path.Add(new string[] { name, value, tooltip });
			}
		}
	}
}
