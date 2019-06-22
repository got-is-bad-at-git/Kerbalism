using System;
using System.Collections.Generic;
using UnityEngine;

namespace KERBALISM
{
	/// <summary>
	/// This data actually is NOT persisted
	/// </summary>
	public sealed class Vessel_info
	{
		/// <summary>
		/// The minimum transmit data rate for "can transmit": 1 bit per second
		/// </summary>
		internal static readonly double MINIMUM_TRANSMIT_DATA_RATE = 1.0 / 1024.0 / 1024.0 / 8;

		public Vessel_info(Vessel v)
		{
			// NOTE: you can't use cached vessel position outside of the cache
			// at different points in time, vessel/body positions are relative to a different frame of reference
			// so comparing the current position of a vessel with the cached one of another makes no sense

			// determine if this is a valid vessel
			is_vessel = Lib.IsVessel(v);
			if (!is_vessel)
				return;

			// determine if this is a rescue mission vessel
			is_rescue = Misc.IsRescueMission(v);
			if (is_rescue)
				return;

			// dead EVA are not valid vessels
			if (EVA.IsDead(v))
				return;

			// shortcut for common tests
			is_valid = true;

			// generate id once
			id = Lib.VesselID(v);

			Update(v);
		}

		public void Update(Vessel v)
		{
			if (!obsolete) return;

			// calculate crew info for the vessel
			crew_count = Lib.CrewCount(v);
			crew_capacity = Lib.CrewCapacity(v);

			// get vessel position
			Vector3d position = Lib.VesselPosition(v);

			// this should never happen again
			if (Vector3d.Distance(position, v.mainBody.position) < 1.0)
			{
				throw new Exception("Shit hit the fan for vessel " + v.vesselName);
			}

			// determine if there is enough EC for a powered state
			powered = Lib.IsPowered(v);

			// determine if in sunlight, calculate sun direction and distance
			sunlight = Sim.RaytraceBody(v, position, FlightGlobals.Bodies[0], out sun_dir, out sun_dist) ? 1.0 : 0.0;

			// environment stuff
			atmo_factor = Sim.AtmosphereFactor(v.mainBody, position, sun_dir);
			gamma_transparency = Sim.GammaTransparency(v.mainBody, v.altitude);
			underwater = Sim.Underwater(v);
			breathable = Sim.Breathable(v, underwater);
			landed = Lib.Landed(v);
			zerog = !landed && (!v.mainBody.atmosphere || v.mainBody.atmosphereDepth < v.altitude);

			// temperature at vessel position
			temperature = Sim.Temperature(v, position, sunlight, atmo_factor, out solar_flux, out albedo_flux, out body_flux, out total_flux);
			temp_diff = Sim.TempDiff(temperature, v.mainBody, landed);

			// radiation
			bool new_inner, new_outer, new_magnetosphere;
			radiation = Radiation.Compute(v, position, gamma_transparency, sunlight, out blackout, out new_magnetosphere, out new_inner, out new_outer, out interstellar);
			// enforce API events on new instances
			if(new_instance || new_inner != inner_belt || new_outer != outer_belt || new_magnetosphere != magnetosphere)
			{
				inner_belt = new_inner;
				outer_belt = new_outer;
				magnetosphere = new_magnetosphere;
				API.OnRadiationFieldChanged.Notify(v, inner_belt, outer_belt, magnetosphere);
			}

			Science.Generate_subject_id(null, null);

			// extended atmosphere
			thermosphere = Sim.InsideThermosphere(v);
			exosphere = Sim.InsideExosphere(v);

			// malfunction stuff
			malfunction = Reliability.HasMalfunction(v);
			critical = Reliability.HasCriticalFailure(v);

			// communications info
			bool old_canTransmit = connection != null && connection.rate > MINIMUM_TRANSMIT_DATA_RATE; // cutoff at 1 bit per second
			string old_transmitting = transmitting;

			connection = ConnectionInfo.Update(v, powered, blackout);
			transmitting = Science.Transmitting(v, connection.linked && connection.rate > double.Epsilon);

			bool new_canTransmit = connection.rate > MINIMUM_TRANSMIT_DATA_RATE;
			if (new_instance || old_canTransmit != new_canTransmit || old_transmitting != transmitting)
			{
				API.OnTransmitStateChanged.Notify(v, transmitting, new_canTransmit);
			}

			// habitat data
			volume = Habitat.Tot_volume(v);
			surface = Habitat.Tot_surface(v);

			if (Cache.HasVesselObjectsCache(v, "max_pressure"))
				max_pressure = Cache.VesselObjectsCache<double>(v, "max_pressure");
			pressure = Math.Min(max_pressure, Habitat.Pressure(v));

			evas = (uint)(Math.Max(0, ResourceCache.Info(v, "Nitrogen").amount - 330) / PreferencesLifeSupport.Instance.evaAtmoLoss);
			poisoning = Habitat.Poisoning(v);
			humidity = Habitat.Humidity(v);
			shielding = Habitat.Shielding(v);
			living_space = Habitat.Living_space(v);
			volume_per_crew = Habitat.Volume_per_crew(v);
			comforts = new Comforts(v, landed, crew_count > 1, connection.linked && connection.rate > double.Epsilon);

			// data about greenhouses
			greenhouses = Greenhouse.Greenhouses(v);

			// other stuff
			gravioli = Sim.Graviolis(v);

			Drive.GetCapacity(v, out free_capacity, out total_capacity);

			if (v.mainBody.flightGlobalsIndex != 0 && TimeWarp.CurrentRate > 1000.0f)
			{
				highspeedWarp(v);
			}

			obsolete = false;
			new_instance = false;
		}

		// at the two highest timewarp speed, the number of sun visibility samples drop to the point that
		// the quantization error first became noticeable, and then exceed 100%, to solve this :
		// - we switch to an analytical estimation of the sunlight/shadow period
		// - atmo_factor become an average atmospheric absorption factor over the daylight period (not the whole day)
		// - we check against timewarp rate, instead of index, to avoid issues during timewarp blending
		public void highspeedWarp(Vessel v, double elapsed_s = 0)
		{
			// don't update every tick but don't allow more than ~1H30 of game time between updates
			if (!is_analytic)
				is_analytic = true;
			else if (elapsed_s < 5000)
				return;

			Vector3d vesselPos = Lib.VesselPosition(v);

			// analytical estimation of the portion of orbit that was in sunlight, current limitations :
			// - the result is dependant on the vessel altitude at the time of evaluation, 
			//   consequently it gives inconsistent behavior with highly eccentric orbits
			// - this totally ignore the orbit inclinaison, polar orbits will be treated as equatorial orbits
			sunlight = 1.0 - Sim.ShadowPeriod(v) / Sim.OrbitalPeriod(v);

			// get solar flux, this can vary a bit but not enough for it to matter much
			solar_flux = Sim.SolarFlux(Sim.SunDistance(vesselPos));

			// for atmospheric bodies whose rotation period is less than 120 hours,
			// determine analytic atmospheric absorption over a single body revolution instead
			// of using a discrete value that would be unreliable at large timesteps :
			if (v.Landed && v.mainBody.atmosphere && v.altitude < v.mainBody.atmosphereDepth)
			{
				Vector3d sunDir = (v.mainBody.position - vesselPos).normalized;
				double atmo_factor_analytic = Sim.AtmosphereFactorAnalytic(v.mainBody, vesselPos, sunDir);
				// determine average flux over a full rotation period
				solar_flux *= sunlight * atmo_factor_analytic;
			}
			else
			{
				// determine average flux for the current altitude
				solar_flux *= sunlight;
			}
		}

		public UInt64 inc;                  // unique incremental id for the entry
		public bool is_vessel;              // true if this is a valid vessel
		public bool is_rescue;              // true if this is a rescue mission vessel
		public bool is_valid;               // equivalent to (is_vessel && !is_rescue && !eva_dead)
		public Guid id;                     // generate the id once
		public int crew_count;              // number of crew on the vessel
		public int crew_capacity;           // crew capacity of the vessel
		public Vector3d sun_dir;            // normalized vector from vessel to sun
		public double sun_dist;             // distance from vessel to sun
		public double albedo_flux;          // solar flux reflected from the nearest body
		public double body_flux;            // infrared radiative flux from the nearest body
		public double total_flux;           // total flux at vessel position
		public double temperature;          // vessel temperature
		public double temp_diff;            // difference between external and survival temperature
		public double radiation;            // environment radiation at vessel position
		public bool magnetosphere;          // true if vessel is inside a magnetopause (except the heliosphere)
		public bool inner_belt;             // true if vessel is inside a radiation belt
		public bool outer_belt;             // true if vessel is inside a radiation belt
		public bool interstellar;           // true if vessel is outside sun magnetopause
		public bool blackout;               // true if the vessel is inside a magnetopause (except the sun) and under storm
		public bool thermosphere;           // true if vessel is inside thermosphere
		public bool exosphere;              // true if vessel is inside exosphere
		public double gamma_transparency;   // proportion of ionizing radiation not blocked by atmosphere
		public bool underwater;             // true if inside ocean
		public bool breathable;             // true if inside breathable atmosphere
		public bool landed;                 // true if on the surface of a body
		public bool zerog;                  // true if in zero g
		public bool malfunction;            // true if at least a component has malfunctioned or had a critical failure
		public bool critical;               // true if at least a component had a critical failure
		public ConnectionInfo connection;   // connection info
		public string transmitting;         // name of file being transmitted, or empty
		public double volume;               // enabled volume in m^3
		public double surface;              // enabled surface in m^2
		public double pressure;             // normalized pressure
		public double max_pressure = 1.0;   // max. attainable pressure on this vessel
		public uint evas;                   // number of EVA's using available Nitrogen
		public double poisoning;            // waste atmosphere amount versus total atmosphere amount
		public double humidity;             // moist atmosphere amount
		public double shielding;            // shielding level
		public double living_space;         // living space factor
		public double volume_per_crew;      // Available volume per crew
		public Comforts comforts;           // comfort info
		public List<Greenhouse.Data> greenhouses; // some data about greenhouses
		public double gravioli;             // gravitation gauge particles detected (joke)
		public bool powered;                // true if vessel is powered
		public double free_capacity = 0.0;  // free data storage available data capacity of all public drives
		public double total_capacity = 0.0; // data capacity of all public drives

		/// <summary>
		/// true when we are timewarping faster than 1000x. When true, some vessel_info fields are updated more frequently
		/// and their evaluation is changed to an analytic, timestep independant mode.
		/// <para/>Ideally they should become a scalar representing an average value valid over a very large duration
		/// and independant of the vessel position changes
		/// </summary>
		public bool is_analytic = false;

		/// <summary>
		/// return 1.0 when the vessel is in direct sunlight, 0.0 when in shadow
		/// <para/> in analytic evaluation, this is a scalar of representing the fraction of time spent in sunlight
		/// </summary>
		// current limitations :
		// - the result is dependant on the vessel altitude at the time of evaluation, 
		//   consequently it gives inconsistent behavior with highly eccentric orbits
		// - this totally ignore the orbit inclinaison, polar orbits will be treated as equatorial orbits
		public double sunlight;

		/// <summary>
		/// solar flux at vessel position in W/m², include atmospheric absorption if inside an atmosphere (atmo_factor)
		/// <para/> zero when the vessel is in shadow while evaluation is non-analytic (low timewarp rates)
		/// <para/> in analytic evaluation, this include fractional sunlight / atmo absorbtion
		/// </summary>
		public double solar_flux;

		/// <summary>
		/// scalar for solar flux absorbtion by atmosphere at vessel position, not meant to be used directly (use solar_flux instead)
		/// <para/> if integrated over orbit (analytic evaluation), average atmospheric absorption factor over the daylight period (not the whole day)
		/// </summary>
		public double atmo_factor;

		public bool obsolete = true;
		public bool new_instance = true;
	}
}