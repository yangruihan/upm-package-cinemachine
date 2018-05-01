using System;
using System.Collections.Generic;
using Cinemachine.Utility;
using UnityEngine;

namespace Cinemachine
{
    /// <summary>
    /// Property applied to CinemachineImpulseManager.EnvelopeDefinition.  
    /// Used for custom drawing in the inspector.
    /// </summary>
    public sealed class CinemachineImpulseEnvelopePropertyAttribute : PropertyAttribute {}

    /// <summary>
    /// Property applied to CinemachineImpulseManager Channels.  
    /// Used for custom drawing in the inspector.
    /// </summary>
    public sealed class CinemachineImpulseChannelPropertyAttribute : PropertyAttribute {}

    /// <summary>
    /// This is a singleton object that manages all Events generated by the Cinemachine
    /// Impulse system.  This singleton owns and manages all ImpulseEvents.
    /// </summary>
    [DocumentationSorting(DocumentationSortingAttribute.Level.API)]
    public class CinemachineImpulseManager
    {
        private static CinemachineImpulseManager sInstance = null;

        /// <summary>Get the singleton instance</summary>
        public static CinemachineImpulseManager Instance
        {
            get
            {
                if (sInstance == null)
                    sInstance = new CinemachineImpulseManager();
                return sInstance;
            }
        }

        const float Epsilon = UnityVectorExtensions.Epsilon;

        /// <summary>This defines the time-envelope of the signal.
        /// Thie raw signal will be scaled to fit inside the envelope.</summary>
        [Serializable]
        public struct EnvelopeDefinition
        {
            /// <summary>Normalized curve defining the shape of the start of the envelope.</summary>
            [Tooltip("Normalized curve defining the shape of the start of the envelope.  If blank a default curve will be used")]
            public AnimationCurve m_AttackShape;

            /// <summary>Normalized curve defining the shape of the end of the envelope.</summary>
            [Tooltip("Normalized curve defining the shape of the end of the envelope.  If blank a default curve will be used")]
            public AnimationCurve m_DecayShape;

            /// <summary>Duration in seconds of the attack.  Attack curve will be scaled to fit.  Must be >= 0</summary>
            [Tooltip("Duration in seconds of the attack.  Attack curve will be scaled to fit.  Must be >= 0.")]
            public float m_AttackTime; // Must be >= 0

            /// <summary>Duration in seconds of the decay.  Decay curve will be scaled to fit.  Must be >= 0.</summary>
            [Tooltip("Duration in seconds of the decay.  Decay curve will be scaled to fit.  Must be >= 0.")]
            public float m_DecayTime; // Must be >= 0

            /// <summary>Duration in seconds of the central fully-scaled part of the envelope.  Must be >= 0.</summary>
            [Tooltip("Duration in seconds of the central fully-scaled part of the envelope.  Must be >= 0.")]
            public float m_HoldTime; // Must be >= 0

            /// <summary>If true, then duration is infinite.</summary>
            [Tooltip("If true, then duration is infinite.")]
            public bool m_HoldForever;

            /// <summary>Duration of the envelope, in seconds.  If negative, then duration is infinite.</summary>
            public float Duration 
            { 
                get 
                { 
                    if (m_HoldForever)
                        return -1;
                    return m_AttackTime + m_HoldTime + m_DecayTime; 
                }
            }

            public float GetValueAt(float offset)
            {
                if (offset >= 0)
                {
                    if (offset < m_AttackTime && m_AttackTime > Epsilon)
                    {
                        if (m_AttackShape == null || m_AttackShape.keys.Length < 2)
                            return Damper.Damp(1, m_AttackTime, offset);
                        return m_AttackShape.Evaluate(offset / m_AttackTime);
                    }
                    offset -= m_AttackTime;
                    if (m_HoldForever || offset < m_HoldTime)
                        return 1;
                    offset -= m_HoldTime;
                    if (offset < m_DecayTime && m_DecayTime > Epsilon)
                    {
                        if (m_DecayShape == null || m_DecayShape.keys.Length < 2)
                            return 1 - Damper.Damp(1, m_DecayTime, offset);
                        return m_DecayShape.Evaluate(offset / m_DecayTime);
                    }
                }
                return 0;
            }

            public void ChangeStopTime(float offset, bool forceNoDecay)
            {
                if (offset < 0)
                    offset = 0;
                if (offset < m_AttackTime)
                    m_AttackTime = 0; // How to prevent pop? GML
                m_HoldTime = offset - m_AttackTime;
                if (forceNoDecay)
                    m_DecayTime = 0;
            }

            public void Clear()
            {
                m_AttackShape = m_DecayShape = null;
                m_AttackTime = m_HoldTime = m_DecayTime = 0;
            }

            public void Validate()
            {
                m_AttackTime = Mathf.Max(0, m_AttackTime);
                m_DecayTime = Mathf.Max(0, m_DecayTime);
                m_HoldTime = Mathf.Max(0, m_HoldTime);
            }
        }

        /// <summary>Interface for raw signal provider</summary>
        /// <param name="pos">The position impulse signal</param>
        /// <param name="rot">The rotation impulse signal</param>
        /// <returns>true if non-trivial signal is returned</returns>
        public interface IRawSignalSource
        {
            bool GetSignal(float timeSinceSignalStart, out Vector3 pos, out Quaternion rot);
        }
        
        /// <summary>Describes an event that generates an impulse signal on one or more channels.
        /// The event has a location in space, a start time, a duration, and a signal.  The signal
        /// will dissipate as the distance from the event location increases.</summary>
        public class ImpulseEvent
        {
            /// <summary>Start time of the event.</summary>
            [Tooltip("Start time of the event.")]
            public float m_StartTime;

            /// <summary>Time-envelope of the signal.</summary>
            [Tooltip("Time-envelope of the signal.")]
            public EnvelopeDefinition m_Envelope;

            /// <summary>Raw signal source.  The ouput of this will be scaled to fit in the envelope.</summary>
            [Tooltip("Raw signal source.  The ouput of this will be scaled to fit in the envelope.")]
            public IRawSignalSource m_SignalSource;

            /// <summary>If true, then duration is infinite.</summary>
            [Tooltip("If true, then duration is infinite.")]
            public Vector3 m_Position;

            /// <summary>Radius around the impact pont that has full signal value.  Distance dissipation begins after this distance.</summary>
            [Tooltip("Radius around the impact pont that has full signal value.  Distance dissipation begins after this distance.")]
            public float m_Radius;

            /// <summary>Channels on which this evvent will broadcast its signal.</summary>
            [Tooltip("Channels on which this evvent will broadcast its signal.")]
            public int m_Channel;

            /// <summary>How the signal dissipates with distance.</summary>
            public enum DissipationMode
            {
                /// <summary>Simple linear interpolation to zero over the dissipation distance.</summary>
                LinearDecay,
                /// <summary>Ease-in-ease-out dissipation over the dissipation distance.</summary>
                SoftDecay,
                /// <summary>Half-life decay, hard out from full and ease into 0 over the dissipation distance.</summary>
                ExponentialDecay
            }

            /// <summary>How the signal dissipates with distance.</summary>
            [Tooltip("How the signal dissipates with distance.")]
            public DissipationMode m_DissipationMode;

            /// <summary>Distance over which the dissipation occurs.  Must be >= 0.</summary>
            [Tooltip("Distance over which the dissipation occurs.  Must be >= 0.")]
            public float m_DissipationDistance;

            /// <summary>Returns true if the event is no longer generating a signal because its time has expired</summary>
            public bool Expired 
            { 
                get 
                { 
                    var d = m_Envelope.Duration; 
                    return d > 0 && m_StartTime + d <= Time.time; 
                }
            }

            /// <summary>Cancel the event at the supplied time</summary>
            /// <param name="time">The time at which to cancel the event</param>
            /// <param name="forceNoDecay">If true, event will be cut immediately at the time, 
            /// otherwise its envelope's decay curve will begin at the cancel time</param>
            public void Cancel(float time, bool forceNoDecay)
            {
                m_Envelope.ChangeStopTime(time - m_StartTime, forceNoDecay);
            }

            /// <summary>Calculate the the decay applicable at a given distance from the impact point</summary>
            /// <returns>Scale factor 0...1</returns>
            public float DistanceDecay(float distance)
            {
                float radius = Mathf.Max(m_Radius, 0);
                if (distance < radius)
                    return 1;
                distance -= radius;
                if (distance >= m_DissipationDistance)
                    return 0;
                switch (m_DissipationMode)
                {
                    default:
                    case DissipationMode.LinearDecay: 
                        return Mathf.Lerp(1, 0, distance / m_DissipationDistance);
                    case DissipationMode.SoftDecay: 
                        return 0.5f * (1 + Mathf.Cos(Mathf.PI * (distance / m_DissipationDistance)));
                    case DissipationMode.ExponentialDecay:
                        return 1 - Damper.Damp(1, m_DissipationDistance, distance);
                }
            }

            /// <summary>Get the signal that a listener at a given position would perceive</summary>
            /// <param name="pos">The position impulse signal</param>
            /// <param name="rot">The rotation impulse signal</param>
            /// <returns>true if non-trivial signal is returned</returns>
            public bool GetDecayedSignal(Vector3 listenerPosition, out Vector3 pos, out Quaternion rot)
            {
                if (m_SignalSource != null)
                {
                    float time = Time.time - m_StartTime;
                    float distance = Vector3.Distance(listenerPosition, m_Position);
                    float scale = m_Envelope.GetValueAt(time) * DistanceDecay(distance);
                    if (!m_SignalSource.GetSignal(time, out pos, out rot))
                        return false;
                    pos *= scale;
                    rot = Quaternion.SlerpUnclamped(Quaternion.identity, rot, scale);
                    return true;
                }
                pos = Vector3.zero;
                rot = Quaternion.identity;
                return false;
            }

            /// <summary>Reset the event to a default state</summary>
            public void Clear()
            {
                m_Envelope.Clear();
                m_StartTime = 0;
                m_SignalSource = null;
                m_Position = Vector3.zero;
                m_Channel = 0;
                m_Radius = 0;
                m_DissipationDistance = 100;
                m_DissipationMode = DissipationMode.ExponentialDecay;
            }

            /// <summary>Don't create them yourself.  Use CinemachineImpulseManager.NewImpulseEvent().</summary> 
            internal ImpulseEvent() {}
        }

        List<ImpulseEvent> m_ExpiredEvents;
        List<ImpulseEvent> m_ActiveEvents;

        /// <summary>Get the signal perceived by a listener at a geven location</summary>
        /// <param name="listenerLocation">Where the listener is, in world coords</param>
        /// <param name="channelMask">Only Impulse signals on channels in this mask will be considered</param>
        /// <param name="pos">The combined position impulse signal resulting from all signals active on the specified channels</param>
        /// <param name="rot">The combined rotation impulse signal resulting from all signals active on the specified channels</param>
        /// <returns>true if non-trivial signal is returned</returns>
        public bool GetImpulseAt(
            Vector3 listenerLocation, int channelMask, 
            out Vector3 pos, out Quaternion rot)
        {
            bool nontrivialResult = false;
            pos = Vector3.zero;
            rot = Quaternion.identity;
            if (m_ActiveEvents != null)
            {
                for (int i = m_ActiveEvents.Count - 1; i >= 0; --i)
                {
                    ImpulseEvent e = m_ActiveEvents[i];
                    // Prune invalid or expired events
                    if (e == null || e.Expired)
                    {
                        m_ActiveEvents.RemoveAt(i);
                        if (e != null)
                        {
                            // Recycle it
                            if (m_ExpiredEvents == null)
                                m_ExpiredEvents = new List<ImpulseEvent>();
                            e.Clear();
                            m_ExpiredEvents.Add(e);
                        }
                    }
                    else if ((e.m_Channel & channelMask) != 0)
                    {
                        Vector3 pos0 = Vector3.zero;
                        Quaternion rot0 = Quaternion.identity;
                        if (e.GetDecayedSignal(listenerLocation, out pos0, out rot0))
                        {
                            nontrivialResult = true;
                            pos += pos0;
                            rot *= rot0;
                        }
                    }
                }
            }
            return nontrivialResult;
        }

        /// <summary>Get a new ImpulseEvent</summary>
        public ImpulseEvent NewImpulseEvent()
        {
            ImpulseEvent e;
            if (m_ExpiredEvents == null || m_ExpiredEvents.Count == 0)
                return new ImpulseEvent();
            e = m_ExpiredEvents[m_ExpiredEvents.Count-1];
            m_ExpiredEvents.RemoveAt(m_ExpiredEvents.Count-1);
            return e;
        }

        /// <summary>Activate an impulse event, so that it may begin broadcasting its signal</summary>
        /// Events will be automatically removed after they expire.
        /// You can tweak the ImpulseEvent fields dynamically if you keep a pointer to it.
        public void AddImpulseEvent(ImpulseEvent e)
        {
            if (m_ActiveEvents == null)
                m_ActiveEvents = new List<ImpulseEvent>();
            if (e != null)
            {
                e.m_StartTime = Time.time;
                m_ActiveEvents.Add(e);
            }
        }

        /// <summary>Immediately terminate all active impulse signals</summary>
        public void Clear()
        {
            if (m_ActiveEvents != null)
            {
                for (int i = 0; i < m_ActiveEvents.Count; ++i)
                    m_ActiveEvents[i].Clear();
                m_ActiveEvents.Clear();
            }
        }
    }
}
