using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Tectonic
{
    public class UpdateLoopService
        : GameService, IUpdateLoopService
    {
        private Dictionary<UpdateStage, List<IUpdatable>> registeredComponents = new Dictionary<UpdateStage, List<IUpdatable>>();
        private List<IUpdatable> componentsToDeregister = new List<IUpdatable>();

        private List<UpdateStage> registeredStages = new List<UpdateStage>();

        private long lastTimestamp;

        public override void Start()
        {
            this.lastTimestamp = Stopwatch.GetTimestamp();
        }

        public void Register(IUpdatable updatableComponent, UpdateStage stage)
        {
            if (stage == UpdateStage.None)
            {
                throw new ArgumentOutOfRangeException(nameof(stage));
            }

            if (!this.registeredComponents.TryGetValue(stage, out var componentList))
            {
                componentList = new List<IUpdatable>();

                this.registeredStages.Add(stage);
                this.registeredStages.Sort();

                this.registeredComponents.Add(stage, componentList);
            }

            componentList.Add(updatableComponent);
        }

        public void Deregister(IUpdatable updatableComponent)
        {
            this.componentsToDeregister.Add(updatableComponent);
        }

        public void RunFrame()
        {
            // This implementation gets a bit weird to avoid any use of Linq,
            // foreach or other enumerator code that would cause repeated
            // allocation/deallocation of managed memory and thus excessive
            // garbage collections.
            // registeredStages is used to hold a sorted duplicate of
            // registeredComponents.Keys for the same reason.

            long timestamp = Stopwatch.GetTimestamp();
            this.DeltaT = (float)((timestamp - this.lastTimestamp) / (double)Stopwatch.Frequency);
            this.lastTimestamp = timestamp;

            for (int stageIndex = 0; stageIndex < this.registeredStages.Count; stageIndex++)
            {
                UpdateStage stage = this.registeredStages[stageIndex];

                for (int componentIndex = 0; componentIndex < this.registeredComponents[stage].Count; componentIndex++)
                {
                    this.registeredComponents[stage][componentIndex].Update();
                }

                for (int componentIndex = 0; componentIndex < this.componentsToDeregister.Count; componentIndex++)
                {
                    for (int removeStageIndex = 0; removeStageIndex < this.registeredStages.Count; removeStageIndex++)
                    {
                        UpdateStage removeStage = this.registeredStages[removeStageIndex];

                        this.registeredComponents[removeStage].Remove(this.componentsToDeregister[componentIndex]);
                    }
                }
            }
        }

        public float DeltaT
        {
            get;
            private set;
        }
    }

    public interface IUpdateLoopService
        : IGameService
    {
        void Register(IUpdatable updatableComponent, UpdateStage stage);

        void Deregister(IUpdatable updatableComponent);

        float DeltaT { get; }
    }

    public enum UpdateStage
    {
        None,
        PreUpdate,
        Update,
        PostUpdate,
        PreRender,
        Render,
        PostRender
    }
}
