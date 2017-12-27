using System;
using System.Collections.Generic;

namespace Tectonic
{
    public class Game
    {
        private readonly IEnumerable<IGameService> services;

        public Game(IEnumerable<IGameService> services)
        {
            this.services = services;
        }

        public GameRunState RunState
        {
            get;
            private set;
        } = GameRunState.PreInitialise;

        public void Initialise()
        {
            this.CheckRunState(GameRunState.PreInitialise);

            foreach (var service in this.services)
            {
                service.Initialise(this);
            }

            this.RunState = GameRunState.Initialised;
        }

        public void Start()
        {
            this.CheckRunState(GameRunState.Initialised);

            foreach (var service in this.services)
            {
                service.Start();
            }

            this.RunState = GameRunState.Running;
        }

        public void SignalStop()
        {
            this.CheckRunState(GameRunState.Running);

            this.RunState = GameRunState.Stopping;
        }

        public void Stop()
        {
            this.CheckRunState(GameRunState.Stopping);

            foreach (var service in this.services)
            {
                service.Stop();
            }

            this.RunState = GameRunState.Stopped;
        }

        private void CheckRunState(GameRunState requiredState)
        {
            if (this.RunState != requiredState)
            {
                throw new InvalidOperationException();
            }
        }
    }

    public enum GameRunState
    {
        PreInitialise,
        Initialised,
        Running,
        Stopping,
        Stopped
    }
}
