﻿//================================================================================================================================
//
//  Copyright (c) 2015-2023 VisionStar Information Technology (Shanghai) Co., Ltd. All Rights Reserved.
//  EasyAR is the registered trademark or trademark of VisionStar Information Technology (Shanghai) Co., Ltd in China
//  and other countries for the augmented reality technology developed by VisionStar Information Technology (Shanghai) Co., Ltd.
//
//================================================================================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace easyar
{
    /// <summary>
    /// <para xml:lang="en"><see cref="MonoBehaviour"/> which controls <see cref="ObjectTracker"/> in the scene, providing a few extensions in the Unity environment. Use <see cref="Tracker"/> directly when necessary.</para>
    /// <para xml:lang="zh">在场景中控制<see cref="ObjectTracker"/>的<see cref="MonoBehaviour"/>，在Unity环境下提供功能扩展。如有需要可以直接使用<see cref="Tracker"/>。</para>
    /// </summary>
    public class ObjectTrackerFrameFilter : FrameFilter, FrameFilter.IFeedbackFrameSink, FrameFilter.IOutputFrameSource
    {
        /// <summary>
        /// <para xml:lang="en">EasyAR Sense API. Accessible after Awake if available.</para>
        /// <para xml:lang="zh">EasyAR Sense API，如果功能可以使用，可以在Awake之后访问。</para>
        /// </summary>
        /// <senseapi/>
        public ObjectTracker Tracker { get; private set; }

        [HideInInspector, SerializeField]
        private int simultaneousNum = 1;
        private List<int> previousTargetIDs = new List<int>();
        private Dictionary<int, TargetController> allTargetController = new Dictionary<int, TargetController>();
        private bool isStarted;
        [HideInInspector, SerializeField]
        private ResultParameters resultType = new ResultParameters();

        /// <summary>
        /// <para xml:lang="en">Target load finish event. The bool value indicates the load success or not.</para>
        /// <para xml:lang="zh">Target加载完成的事件。bool值表示加载是否成功。</para>
        /// </summary>
        public event Action<ObjectTargetController, Target, bool> TargetLoad;
        /// <summary>
        /// <para xml:lang="en">Target unload finish event. The bool value indicates the unload success or not.</para>
        /// <para xml:lang="zh">Target卸载完成的事件。bool值表示卸载是否成功。</para>
        /// </summary>
        public event Action<ObjectTargetController, Target, bool> TargetUnload;
        private event Action SimultaneousNumChanged;

        public override int BufferRequirement
        {
            get { return Tracker.bufferRequirement(); }
        }

        /// <summary>
        /// <para xml:lang="en">The max number of targets which will be the simultaneously tracked by the tracker. Modify at any time and takes effect immediately.</para>
        /// <para xml:lang="zh">最大可被tracker跟踪的目标个数。可随时修改，立即生效。</para>
        /// </summary>
        public int SimultaneousNum
        {
            get
            {
                if (Tracker == null)
                    return simultaneousNum;
                return Tracker.simultaneousNum();
            }
            set
            {
                if (Tracker == null)
                {
                    simultaneousNum = value;
                    return;
                }
                simultaneousNum = value;
                Tracker.setSimultaneousNum(simultaneousNum);
                if (SimultaneousNumChanged != null)
                {
                    SimultaneousNumChanged();
                }
            }
        }

        /// <summary>
        /// <para xml:lang="en"><see cref="TargetController"/> that has been loaded.</para>
        /// <para xml:lang="zh">已加载的<see cref="TargetController"/>。</para>
        /// </summary>
        public List<TargetController> TargetControllers
        {
            get
            {
                List<TargetController> list = new List<TargetController>();
                foreach (var value in allTargetController.Values)
                {
                    list.Add(value);
                }
                return list;
            }
            private set { }
        }

        /// <summary>
        /// <para xml:lang="en">Result parameters.</para>
        /// <para xml:lang="zh">结果参数。</para>
        /// </summary>
        public ResultParameters ResultType
        {
            get => resultType;
            set
            {
                resultType = value;
                Tracker?.setResultPostProcessing(resultType.EnablePersistentTargetInstance, resultType.EnableMotionFusion);
            }
        }

        protected virtual void Awake()
        {
            if (!EasyARController.Initialized)
            {
                return;
            }
            if (!ObjectTracker.isAvailable())
            {
                throw new UIPopupException(typeof(ObjectTracker) + " not available");
            }

            Tracker = ObjectTracker.create();
            Tracker.setSimultaneousNum(simultaneousNum);
            Tracker.setResultPostProcessing(ResultType.EnablePersistentTargetInstance, ResultType.EnableMotionFusion);
        }

        protected virtual void OnEnable()
        {
            if (Tracker != null && isStarted)
            {
                Tracker.start();
            }
        }

        protected virtual void OnDisable()
        {
            if (Tracker != null)
            {
                Tracker.stop();
            }
        }

        protected virtual void OnDestroy()
        {
            foreach (var value in TargetControllers)
            {
                if (value is ObjectTargetController)
                {
                    UnloadTarget(value as ObjectTargetController);
                }
            }
            if (Tracker != null)
            {
                Tracker.Dispose();
            }
        }

        /// <summary>
        /// <para xml:lang="en">Load target.</para>
        /// <para xml:lang="zh">加载target。</para>
        /// </summary>
        public void LoadTarget(ObjectTargetController target)
        {
            if (target.Target != null && TryGetTargetController(target.Target.runtimeID()))
            {
                return;
            }
            target.Tracker = this;
        }

        /// <summary>
        /// <para xml:lang="en">Unload target.</para>
        /// <para xml:lang="zh">卸载target。</para>
        /// </summary>
        public void UnloadTarget(ObjectTargetController target)
        {
            if (target.Target != null && !TryGetTargetController(target.Target.runtimeID()))
            {
                return;
            }
            target.Tracker = null;
        }

        public FeedbackFrameSink FeedbackFrameSink()
        {
            if (Tracker != null)
            {
                return Tracker.feedbackFrameSink();
            }
            return null;
        }

        public OutputFrameSource OutputFrameSource()
        {
            if (Tracker != null)
            {
                return Tracker.outputFrameSource();
            }
            return null;
        }

        public override void OnAssemble(ARSession session)
        {
            base.OnAssemble(session);
            SimultaneousNumChanged += session.Assembly.ResetBufferCapacity;

            isStarted = true;
            if (enabled)
            {
                OnEnable();
            }
        }

        public void OnResult(Optional<FrameFilterResult> frameFilterResult)
        {
            var resultControllers = new List<Tuple<TargetController, Pose>>();
            var targetIDs = new List<int>();
            var lostIDs = new List<int>();

            if (frameFilterResult.OnSome)
            {
                var targetTrackerResult = frameFilterResult.Value as TargetTrackerResult;
                foreach (var targetInstance in targetTrackerResult.targetInstances())
                {
                    using (targetInstance)
                    using (var target = targetInstance.target())
                    {
                        var controller = TryGetTargetController(target.runtimeID());
                        if (controller)
                        {
                            targetIDs.Add(target.runtimeID());
                            resultControllers.Add(Tuple.Create(controller, targetInstance.pose().ToUnityPose()));
                        }
                    }
                }
            }

            foreach (var id in previousTargetIDs)
            {
                lostIDs.Add(id);
            }
            foreach (var id in targetIDs)
            {
                if (lostIDs.Contains(id))
                {
                    lostIDs.Remove(id);
                }
                var controller = TryGetTargetController(id);
                if (controller && controller.IsLoaded)
                {
                    controller.OnTracking(true);
                }
            }
            foreach (var id in lostIDs)
            {
                var controller = TryGetTargetController(id);
                if (controller)
                {
                    controller.OnTracking(false);
                }
            }
            previousTargetIDs = targetIDs;
            targetResults = resultControllers;
        }

        public override Optional<Tuple<GameObject, Pose>> TryGetCenter(GameObject center) => TryGetCenterTarget(center);

        public override void UpdateTransform(GameObject center, Pose centerPose) => UpdateTargetTransform(center, centerPose);

        internal void LoadObjectTarget(ObjectTargetController controller, Action<Target, bool> callback)
        {
            Tracker.loadTarget(controller.Target, EasyARController.Scheduler, (target, status) =>
            {
                if (TargetLoad != null)
                {
                    TargetLoad(controller, target, status);
                }
                if (callback != null)
                {
                    callback(target, status);
                }
            });
            allTargetController[controller.Target.runtimeID()] = controller;
        }

        internal void UnloadObjectTarget(ObjectTargetController controller, Action<Target, bool> callback)
        {
            if (allTargetController.Remove(controller.Target.runtimeID()))
            {
                controller.OnTracking(false);
                Tracker.unloadTarget(controller.Target, EasyARController.Scheduler, (target, status) =>
                {
                    if (TargetUnload != null)
                    {
                        TargetUnload(controller, target, status);
                    }
                    if (callback != null)
                    {
                        callback(target, status);
                    }
                });
            }
        }

        protected override void OnHFlipChange(bool hFlip)
        {
            foreach (var value in allTargetController.Values)
            {
                value.HorizontalFlip = hFlip;
            }
        }

        private TargetController TryGetTargetController(int runtimeID)
        {
            TargetController controller;
            if (allTargetController.TryGetValue(runtimeID, out controller))
                return controller;
            return null;
        }

        /// <summary>
        /// <para xml:lang="en">Result parameters.</para>
        /// <para xml:lang="zh">结果参数。</para>
        /// </summary>
        [Serializable]
        public class ResultParameters
        {
            /// <summary>
            /// <para xml:lang="en">When it is enabled and InputFrame contains spatial information, all recognized instances (with not tracking target instances) will be updated.</para>
            /// <para xml:lang="zh">开启时，如果 InputFrame 数据中包含空间信息，则会更新所有识别到的instance(包括当前未跟踪的)。</para>
            /// </summary>
            public bool EnablePersistentTargetInstance = false;
            /// <summary>
            /// <para xml:lang="en">Enable motion fusion.</para>
            /// <para xml:lang="en">Motion fusion will only work when a) one type of motion tracking is running and b) target scale is set to the scale in real world and c) the target does not move in real world.</para>
            /// <para xml:lang="zh">开启运动融合功能。</para>
            /// <para xml:lang="zh">运动融合只在满足以下条件时可以工作：1）任意一种运动跟踪功能在运行，2）target scale 与真实世界中的数值相同，3）target在真实世界中不会移动。</para>
            /// </summary>
            public bool EnableMotionFusion = false;
        }
    }
}
