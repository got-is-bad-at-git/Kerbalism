using System;
using System.Collections.Generic;
using Experience;
using UnityEngine;
using KSP.Localization;
using System.Collections;


namespace KERBALISM
{
	/// <summary>
	/// A global, static tracker for experiment state information on all vessels
	/// </summary>
	public static class ExperimentTracker
	{
		// this is called by the experiment part module and automation tab.
		public static void Update(Vessel v, string experiment_id, Experiment.State state)
		{
			bool isRunning = state == Experiment.State.RUNNING;

			var experimentStateInfo = Info(Lib.VesselID(v), experiment_id);
			bool wasRunning = experimentStateInfo.state == Experiment.State.RUNNING;

			bool doNotify = isRunning != wasRunning || experimentStateInfo.state == Experiment.State.UNKNOWN;

			experimentStateInfo.state = state;

			if(doNotify) API.OnExperimentStateChanged.Notify(v, experiment_id, isRunning);
		}

		public static ExperimentStateInfo Info(Guid vessel_id, string experiment_id)
		{
			Dictionary<string, ExperimentStateInfo> stateInfos;
			if (!globalState.TryGetValue(vessel_id, out stateInfos))
			{
				stateInfos = new Dictionary<string, ExperimentStateInfo>();
				globalState.Add(vessel_id, stateInfos);
			}

			ExperimentStateInfo stateInfo;
			if (!stateInfos.TryGetValue(experiment_id, out stateInfo))
			{
				stateInfo = new ExperimentStateInfo();
				stateInfos.Add(experiment_id, stateInfo);
			}

			return stateInfo;
		}

		public class ExperimentStateInfo
		{
			public Experiment.State state = Experiment.State.UNKNOWN;
			// add other stuff to track here...
		}


		// TODO make sure we track part/vessel destroyed events and docking/undocking

		public static readonly Dictionary<Guid, Dictionary<string, ExperimentStateInfo>> globalState = new Dictionary<Guid, Dictionary<string, ExperimentStateInfo>>();
	}
}
