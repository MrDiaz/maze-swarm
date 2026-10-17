using System;
using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // Engineer presentation only. Actual displaced distance drives positive gait phase;
    // gameplay movement, aim, damage, progression and the Heavy are untouched.
    public sealed class EngineerHeroPlayback
    {
        readonly Animation animation;
        readonly AnimationState idle, walk, jog, run, dash, attack, death;
        readonly AnimationState[] states;
        readonly Transform spine;
        readonly Leg left, right;
        float gaitPhase, idleClock, attackClock, movementBlend, jogBlend, runBlend, attackWeight, deathTime, dashTime, dashBlend;
        float speed, contactDuty, walkingWeight;
        bool forwardTravel = true;
        bool wasDashing;
        public float GaitPlaybackRate { get; private set; }
        public float ResidualGroundSpeed { get; private set; }
        public float GaitPhase => gaitPhase;
        public Vector3 LeftToeWorld => left != null && left.toeBone != null ? left.toeBone.TransformPoint(left.toeTipLocal) : Vector3.zero;
        public Vector3 RightToeWorld => right != null && right.toeBone != null ? right.toeBone.TransformPoint(right.toeTipLocal) : Vector3.zero;
        public Vector3 LeftHeelWorld => left != null ? left.foot.TransformPoint(left.heel) : Vector3.zero;
        public Vector3 RightHeelWorld => right != null ? right.foot.TransformPoint(right.heel) : Vector3.zero;
        public bool LeftContact => !Dead && !wasDashing && Mathf.Repeat(gaitPhase - .5f, 1) < contactDuty;
        public bool RightContact => !Dead && !wasDashing && gaitPhase < contactDuty;
        public bool LeftPlanted => left != null && left.planted;
        public bool RightPlanted => right != null && right.planted;
        public float LeftPlantError => left?.error ?? 0;
        public float RightPlantError => right?.error ?? 0;
        public float MaximumLegCorrection { get; private set; }
        public bool Dead { get; private set; }
        public bool Ready => idle != null && walk != null && jog != null && run != null && dash != null && attack != null && death != null;
        public string ActiveClip { get; private set; }
        public float AttackWeight => attackWeight;
        public float DeathTime => deathTime;

        sealed class Leg
        {
            public Transform hip, knee, foot, toeBone;
            public Vector3 sole, heel, toe, toeTipLocal, anchor, forward;
            public int contactKind;
            public bool planted; public float error;
        }
        public EngineerHeroPlayback(Animation value, Transform model)
        {
            animation = value;
            if (animation == null) return;
            idle=Find("Idle");walk=Find("Walking");jog=Find("Jogging");run=Find("Running");dash=Find("Dash");attack=Find("Attack");death=Find("Death");
            var all=new List<AnimationState>();foreach(AnimationState state in animation)all.Add(state);states=all.ToArray();
            animation.Stop();animation.enabled=false;
            foreach(var bone in model.GetComponentsInChildren<Transform>(true))
                if(bone.name=="Spine"||bone.name.EndsWith(":Spine",StringComparison.Ordinal)){spine=bone;break;}
            if(attack!=null&&spine!=null){attack.layer=1;attack.blendMode=AnimationBlendMode.Blend;attack.AddMixingTransform(spine,true);}
            if(!Ready||spine==null){Debug.LogError("Engineer requires Idle/Walking/Jogging/Running/Dash/Attack/Death Legacy clips and Spine.");return;}
            Tick(0,0,false,false);
            left=FindLeg(model,"Left");right=FindLeg(model,"Right");
        }
        AnimationState Find(string name)
        {
            foreach(AnimationState state in animation)
                if(state.name.Equals(name,StringComparison.OrdinalIgnoreCase)||state.name.EndsWith("|"+name,StringComparison.OrdinalIgnoreCase))return state;
            return null;
        }
        static void Base(AnimationState state,float weight,float time, bool clamp=false)
        {state.layer=0;state.wrapMode=clamp?WrapMode.ClampForever:WrapMode.Loop;state.enabled=weight>0;state.weight=weight;state.time=time;}
        public void Tick(float dt,float movementSpeed,bool firing,bool dead,float direction=1,bool dashing=false)
        {
            if(!Ready)return;dt=Mathf.Max(0,dt);speed=Mathf.Max(0,movementSpeed);
            if(dead&&!Dead){deathTime=0;attackWeight=0;ReleaseFeet();}
            if(!dead&&Dead){gaitPhase=idleClock=attackClock=movementBlend=jogBlend=runBlend=deathTime=attackWeight=dashBlend=0;ReleaseFeet();}
            Dead=dead;forwardTravel=direction>.2f;
            foreach(var state in states){state.enabled=false;state.weight=0;state.speed=0;}
            if(dead){deathTime=Mathf.Min(death.length,deathTime+dt);Base(death,1,deathTime,true);ActiveClip=death.name;}
            else
            {
                if(dashing&&!wasDashing){dashTime=0;ReleaseFeet();}
                if(dashing)dashTime=Mathf.Min(dash.length,dashTime+dt);
                dashBlend=Mathf.MoveTowards(dashBlend,dashing?1:0,dt/.05f);wasDashing=dashing;
                movementBlend=Mathf.MoveTowards(movementBlend,speed>.08f?1:0,dt/.1f);
                jogBlend=Mathf.MoveTowards(jogBlend,Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.05f,2.05f,speed)),dt/.12f);
                runBlend=Mathf.MoveTowards(runBlend,Mathf.SmoothStep(0,1,Mathf.InverseLerp(3.1f,5.5f,speed)),dt/.12f);
                float rw=runBlend,jw=(1-rw)*jogBlend,ww=1-rw-jw;walkingWeight=ww;
                float period=walk.length*ww+jog.length*jw+run.length*rw;
                float distance=CohesiveCharacterPolicy.EngineerWalkGroundSpeed*walk.length*ww+CohesiveCharacterPolicy.EngineerJogGroundSpeed*jog.length*jw+CohesiveCharacterPolicy.EngineerRunGroundSpeed*run.length*rw;
                float native=distance/Mathf.Max(.01f,period);
                GaitPlaybackRate=speed>.08f&&!dashing?speed/Mathf.Max(.01f,native):0;
                ResidualGroundSpeed=0; // No arbitrary 3x cap: the new stride matches measured runtime speeds.
                if(!dashing)gaitPhase=Mathf.Repeat(gaitPhase+dt*speed/Mathf.Max(.01f,distance),1);
                contactDuty=.55f;
                if(jw>.1f)contactDuty=Mathf.Min(contactDuty,.2f);
                if(rw>.1f)contactDuty=.1f;
                idleClock+=dt;attackClock+=dt;
                float moving=movementBlend*(1-dashBlend);
                Base(idle,(1-movementBlend)*(1-dashBlend),Mathf.Repeat(idleClock,idle.length));
                Base(walk,moving*ww,gaitPhase*walk.length);Base(jog,moving*jw,gaitPhase*jog.length);Base(run,moving*rw,gaitPhase*run.length);
                Base(dash,dashBlend,dashTime,true);
                ActiveClip=dashBlend>.5f?dash.name:movementBlend<.5f?idle.name:rw>.5f?run.name:jw>.5f?jog.name:walk.name;
                attackWeight=Mathf.MoveTowards(attackWeight,firing?1:0,dt/.12f);
                if(attackWeight>0){attack.wrapMode=WrapMode.Loop;attack.enabled=true;attack.weight=attackWeight;attack.time=Mathf.Repeat(attackClock,attack.length);}
            }
            animation.Sample();
        }
        public void Aim(float deltaDegrees)
        {
            if(!Dead&&spine!=null)spine.rotation=Quaternion.AngleAxis(deltaDegrees,Vector3.up)*spine.rotation;
        }
        public void ResetContacts()=>ReleaseFeet();
        void ReleaseFeet(){if(left!=null)left.planted=false;if(right!=null)right.planted=false;}
        public void PlaceFeet()
        {
            MaximumLegCorrection=0;
            if(Dead||!forwardTravel||wasDashing||dashBlend>.1f||movementBlend<.95f||speed<.1f){ReleaseFeet();return;}
            Plant(right,0);Plant(left,.5f);
        }
        void Plant(Leg leg,float strike)
        {
            if(leg==null)return;
            float phase=Mathf.Repeat(gaitPhase-strike,1);
            leg.error=0;if(phase>=contactDuty){leg.planted=false;return;}
            var toeLocal=leg.toeBone!=null?leg.foot.InverseTransformPoint(leg.toeBone.TransformPoint(leg.toeTipLocal)):leg.toe;
            // Exact source contract: walking rolls heel -> flat Foot -> Toe.
            // Jogging/running load the pure Toe pad throughout their short stance.
            float stance=phase/contactDuty;
            int kind=walkingWeight>.85f?(stance<.16f?0:stance<.73f?1:2):2;
            Vector3 Pivot(int value)=>value==0?leg.heel:value==1?leg.sole:toeLocal;
            var pivot=Pivot(kind);
            var currentForward=Vector3.ProjectOnPlane(leg.foot.forward,Vector3.up).normalized;
            if(!leg.planted)
            {
                leg.anchor=leg.foot.TransformPoint(pivot);leg.forward=currentForward;
                leg.contactKind=kind;leg.planted=true;
            }
            var footRotation=Quaternion.FromToRotation(currentForward,leg.forward)*leg.foot.rotation;
            if(kind!=leg.contactKind)
            {
                // Transfer support between real material points without a positional snap.
                leg.anchor+=footRotation*Vector3.Scale(pivot-Pivot(leg.contactKind),leg.foot.lossyScale);
                leg.contactKind=kind;
            }
            var goal=leg.anchor;
            // Markers sit above the lowest curved sole by a few millimetres. Preserve
            // the sampled animation's Y/roll reference; never force every point to0.
            goal.y=leg.foot.TransformPoint(pivot).y;
            var ankleGoal=goal-footRotation*Vector3.Scale(pivot,leg.foot.lossyScale);
            MaximumLegCorrection=Mathf.Max(MaximumLegCorrection,Vector3.Distance(ankleGoal,leg.foot.position));
            // Fixed bone lengths, with anatomical reach clamping. No leg scaling.
            Solve(leg,ankleGoal);leg.foot.rotation=footRotation;
            leg.error=Vector3.Distance(leg.foot.TransformPoint(pivot),goal);
        }
        static void Solve(Leg leg,Vector3 target)
        {
            Vector3 a=leg.hip.position,b=leg.knee.position,c=leg.foot.position;
            float upper=Vector3.Distance(a,b),lower=Vector3.Distance(b,c);var delta=target-a;float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(upper-lower)+.0001f,upper+lower-.0001f);var dir=delta.normalized;
            var bend=Vector3.ProjectOnPlane(b-a,dir).normalized;if(bend.sqrMagnitude<.01f)bend=Vector3.ProjectOnPlane(leg.hip.forward,dir).normalized;
            float along=(upper*upper+distance*distance-lower*lower)/(2*distance);float outward=Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
            var knee=a+dir*along+bend*outward;
            leg.hip.rotation=Quaternion.FromToRotation(b-a,knee-a)*leg.hip.rotation;
            leg.knee.rotation=Quaternion.FromToRotation(leg.foot.position-leg.knee.position,target-leg.knee.position)*leg.knee.rotation;
        }
        static Leg FindLeg(Transform model,string side)
        {
            Transform foot=null;foreach(var t in model.GetComponentsInChildren<Transform>(true))if(t.name==side+"Foot"){foot=t;break;}
            if(foot?.parent?.parent==null)return null;
            var leg=new Leg{foot=foot,knee=foot.parent,hip=foot.parent.parent};
            foreach(var t in foot.GetComponentsInChildren<Transform>(true))if(t.name==side+"Toe")leg.toeBone=t;
            // Exact actual-surface landmarks from approved gait011 CONTACT-MANIFEST.
            // Foot heel is rigid Foot; toe pad is rigid Toe. No phone mesh readback.
            if (side == "Right")
            {
                leg.sole=new Vector3(0.0637183040f,-0.0675845220f,0.0233479869f);
                leg.heel=new Vector3(-0.0107483864f,-0.0662620766f,-0.0448934454f);
                leg.toeTipLocal=new Vector3(-0.0102723390f,-0.0235790520f,0.0587442145f);
                leg.toe=leg.toeBone!=null?foot.InverseTransformPoint(leg.toeBone.TransformPoint(leg.toeTipLocal)):leg.sole;
            }
            else if (side == "Left")
            {
                leg.sole=new Vector3(0.0010862648f,-0.0677545182f,-0.0115186963f);
                leg.heel=new Vector3(0.0125543475f,-0.0670425517f,-0.1035547573f);
                leg.toeTipLocal=new Vector3(0.0028638989f,-0.0237440442f,0.0622332245f);
                leg.toe=leg.toeBone!=null?foot.InverseTransformPoint(leg.toeBone.TransformPoint(leg.toeTipLocal)):leg.sole;
            }
            return leg;
        }
    }
}
