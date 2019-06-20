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
		public enum ExperimentState { RUNNING, STOPPED, UNKNOWN }
		public static readonly ExperimentStateChanged Changed = new ExperimentStateChanged();

		public class ExperimentStateChanged
		{
			// This is the list of methods that should be activated when the event fires
			internal List<Action<Guid, string, ExperimentState>> receivers = new List<Action<Guid, string, ExperimentState>>();

			// This adds a connection info handler
			public void Add(Action<Guid, string, ExperimentState> receiver)
			{
				if (!receivers.Contains(receiver)) receivers.Add(receiver);
			}

			// This removes a connection info handler
			public void Remove(Action<Guid, string, ExperimentState> receiver)
			{
				if (receivers.Contains(receiver)) receivers.Remove(receiver);
			}

			public void Notify(Guid vessel_id, string experiment_id, ExperimentState state)
			{
				foreach (Action<Guid, string, ExperimentState> receiver in receivers)
				{
					receiver.Invoke(vessel_id, experiment_id, state);
				}
			}
		}

		// this is called by the experiment part module and automation tab.
		public static void Update(Guid vessel_id, string experiment_id, Experiment.State state)
		{
			ExperimentState newState = ExperimentState.STOPPED;

			if(state == Experiment.State.RUNNING) newState = ExperimentState.RUNNING;

			var experimentStateInfo = Info(vessel_id, experiment_id);

			if(newState != experimentStateInfo.state)
			{
				experimentStateInfo.state = newState;
				Changed.Notify(vessel_id, experiment_id, newState);
			}
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
			public ExperimentState state = ExperimentState.UNKNOWN;
			// add other stuff to track here...
		}


		// TODO make sure we track part/vessel destroyed events and docking/undocking

		public static readonly Dictionary<Guid, Dictionary<string, ExperimentStateInfo>> globalState = new Dictionary<Guid, Dictionary<string, ExperimentStateInfo>>();
	}
}
