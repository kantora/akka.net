//-----------------------------------------------------------------------
// <copyright file="PersistentFSMSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Persistence.Fsm;
using Xunit;

namespace Akka.Persistence.Tests.Fsm
{
    public class PersistentFSMAsyncSpec : PersistenceSpec
    {
        private readonly Random _random = new Random();

        public PersistentFSMAsyncSpec()
            : base(Configuration("PersistentFSMSpec"))
        {
        }

        [Fact]
        public void PersistentFSM_should_has_function_as_regular_fsm()
        {
            var dummyReportActorRef = CreateTestProbe().Ref;

            var fsmRef = Sys.ActorOf(Props.Create<WebStoreCustomerFSMAsync>(Name, dummyReportActorRef), Name);

            Watch(fsmRef);
            fsmRef.Tell(new FSMBase.SubscribeTransitionCallBack(TestActor));

            var shirt = new Item("1", "Shirt", 59.99F);
            var shoes = new Item("2", "Shoes", 89.99F);
            var coat = new Item("3", "Coat", 119.99F);

            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(shirt));
            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(shoes));
            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(coat));
            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new Buy());
            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new Leave());
            ExpectMsg<FSMBase.CurrentState<UserState>>(state => state.State == UserState.LookingAround);
            ExpectMsg<EmptyShoppingCart>();
            ExpectMsg<FSMBase.Transition<UserState>>(state => state.From == UserState.LookingAround);
            ExpectMsg<NonEmptyShoppingCart>(
                cart => cart.Items.Any(i => i.Name == "Shirt") && cart.Items.Count == 1);
            ExpectMsg<NonEmptyShoppingCart>(
                cart => cart.Items.Any(i => i.Name == "Shoes") && cart.Items.Count == 2);
            ExpectMsg<NonEmptyShoppingCart>(
                cart => cart.Items.Any(i => i.Name == "Coat") && cart.Items.Count == 3);
            ExpectMsg<FSMBase.Transition<UserState>>();
            ExpectMsg<NonEmptyShoppingCart>();
            ExpectTerminated(fsmRef);
        }

        [Fact]
        public void PersistentFSM_should_has_function_as_regular_fsm_on_state_timeout()
        {
            var dummyReportActorRef = CreateTestProbe().Ref;

            var fsmRef = Sys.ActorOf(Props.Create<WebStoreCustomerFSMAsync>(Name, dummyReportActorRef), Name);

            Watch(fsmRef);

            fsmRef.Tell(new FSMBase.SubscribeTransitionCallBack(TestActor));

            var shirt = new Item("1", "Shirt", 59.99F);

            fsmRef.Tell(new AddItem(shirt));
            ExpectMsg<FSMBase.CurrentState<UserState>>(state => state.State == UserState.LookingAround);

            ExpectMsg<FSMBase.Transition<UserState>>();

            Within(TimeSpan.FromSeconds(0.9), TimeSpan.FromSeconds(1.9), () =>
            {
                ExpectMsg<FSMBase.Transition<UserState>>();
                return true;
            });

            ExpectTerminated(fsmRef);
        }

        [Fact]
        public void PersistentFSM_should_recover_successfully_with_correct_state_data()
        {
            var dummyReportActorRef = CreateTestProbe().Ref;

            var fsmRef = Sys.ActorOf(Props.Create(() => new WebStoreCustomerFSMAsync(Name, dummyReportActorRef)));

            Watch(fsmRef);
            fsmRef.Tell(new FSMBase.SubscribeTransitionCallBack(TestActor));

            var shirt = new Item("1", "Shirt", 59.99F);
            var shoes = new Item("2", "Shoes", 89.99F);
            var coat = new Item("3", "Coat", 119.99F);

            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(shirt));
            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(shoes));
            fsmRef.Tell(new GetCurrentCart());


            ExpectMsg<FSMBase.CurrentState<UserState>>();
            ExpectMsg<EmptyShoppingCart>();
            ExpectMsg<FSMBase.Transition<UserState>>(state => state.From == UserState.LookingAround);
            ExpectMsg<NonEmptyShoppingCart>(
                cart => cart.Items.Any(i => i.Name == "Shirt") && cart.Items.Count == 1);
            ExpectMsg<NonEmptyShoppingCart>(
                cart => cart.Items.Any(i => i.Name == "Shoes") && cart.Items.Count == 2);

            fsmRef.Tell(PoisonPill.Instance);
            ExpectTerminated(fsmRef);

            var recoveredFsmRef = Sys.ActorOf(Props.Create(() => new WebStoreCustomerFSMAsync(Name, dummyReportActorRef)));
            Watch(recoveredFsmRef);
            recoveredFsmRef.Tell(new FSMBase.SubscribeTransitionCallBack(TestActor));

            recoveredFsmRef.Tell(new GetCurrentCart());
            recoveredFsmRef.Tell(new AddItem(coat));
            recoveredFsmRef.Tell(new GetCurrentCart());
            recoveredFsmRef.Tell(new Buy());
            recoveredFsmRef.Tell(new GetCurrentCart());
            recoveredFsmRef.Tell(new Leave());

            ExpectMsg<FSMBase.CurrentState<UserState>>(state => state.State == UserState.Shopping);
            ExpectMsg<NonEmptyShoppingCart>(
                cart => cart.Items.Any(i => i.Name == "Shoes") && cart.Items.Count == 2);
            ExpectMsg<NonEmptyShoppingCart>(
                cart => cart.Items.Any(i => i.Name == "Coat") && cart.Items.Count == 3);
            ExpectMsg<FSMBase.Transition<UserState>>();
            ExpectMsg<NonEmptyShoppingCart>();
            ExpectTerminated(recoveredFsmRef);
        }

        [Fact]
        public void PersistentFSM_should_execute_the_defined_actions_following_successful_persistence_of_state_change()
        {
            var reportActorProbe = CreateTestProbe(Sys);

            var fsmRef = Sys.ActorOf(Props.Create(() => new WebStoreCustomerFSMAsync(Name, reportActorProbe.Ref)));

            Watch(fsmRef);
            fsmRef.Tell(new FSMBase.SubscribeTransitionCallBack(TestActor));

            var shirt = new Item("1", "Shirt", 59.99F);
            var shoes = new Item("2", "Shoes", 89.99F);
            var coat = new Item("3", "Coat", 119.99F);

            fsmRef.Tell(new AddItem(shirt));
            fsmRef.Tell(new AddItem(shoes));
            fsmRef.Tell(new AddItem(coat));
            fsmRef.Tell(new Buy());
            fsmRef.Tell(new Leave());

            ExpectMsg<FSMBase.CurrentState<UserState>>(state => state.State == UserState.LookingAround);
            ExpectMsg<FSMBase.Transition<UserState>>(
                state => state.From == UserState.LookingAround && state.To == UserState.Shopping);
            ExpectMsg<FSMBase.Transition<UserState>>(
                state => state.From == UserState.Shopping && state.To == UserState.Paid);
            reportActorProbe.ExpectMsg<PurchaseWasMade>();
            ExpectTerminated(fsmRef);
        }

        [Fact]
        public void PersistentFSM_should_execute_the_defined_actions_following_successful_persistence_of_FSM_stop()
        {
            var reportActorProbe = CreateTestProbe(Sys);

            var fsmRef = Sys.ActorOf(Props.Create(() => new WebStoreCustomerFSMAsync(Name, reportActorProbe.Ref)));

            Watch(fsmRef);
            fsmRef.Tell(new FSMBase.SubscribeTransitionCallBack(TestActor));

            var shirt = new Item("1", "Shirt", 59.99F);
            var shoes = new Item("2", "Shoes", 89.99F);
            var coat = new Item("3", "Coat", 119.99F);

            fsmRef.Tell(new AddItem(shirt));
            fsmRef.Tell(new AddItem(shoes));
            fsmRef.Tell(new AddItem(coat));
            fsmRef.Tell(new Leave());

            ExpectMsg<FSMBase.CurrentState<UserState>>(state => state.State == UserState.LookingAround);
            ExpectMsg<FSMBase.Transition<UserState>>(
                state => state.From == UserState.LookingAround && state.To == UserState.Shopping);
            reportActorProbe.ExpectMsg<ShoppingCardDiscarded>();
            ExpectTerminated(fsmRef);
        }

        [Fact]
        public void PersistentFSM_should_recover_successfully_with_correct_state_timeout()
        {
            var dummyReportActorRef = CreateTestProbe().Ref;

            var fsmRef = Sys.ActorOf(Props.Create(() => new WebStoreCustomerFSMAsync(Name, dummyReportActorRef)));

            Watch(fsmRef);
            fsmRef.Tell(new FSMBase.SubscribeTransitionCallBack(TestActor));

            var shirt = new Item("1", "Shirt", 59.99F);

            fsmRef.Tell(new AddItem(shirt));

            ExpectMsg<FSMBase.CurrentState<UserState>>(state => state.State == UserState.LookingAround);
            ExpectMsg<FSMBase.Transition<UserState>>(
                state => state.From == UserState.LookingAround && state.To == UserState.Shopping);

            ExpectNoMsg(TimeSpan.FromSeconds(0.6));
            fsmRef.Tell(PoisonPill.Instance);
            ExpectTerminated(fsmRef);

            var recoveredFsmRef = Sys.ActorOf(Props.Create(() => new WebStoreCustomerFSMAsync(Name, dummyReportActorRef)));
            Watch(recoveredFsmRef);
            recoveredFsmRef.Tell(new FSMBase.SubscribeTransitionCallBack(TestActor));

            ExpectMsg<FSMBase.CurrentState<UserState>>(state => state.State == UserState.Shopping);


            Within(TimeSpan.FromSeconds(0.9), TimeSpan.FromSeconds(1.9), () =>
            {
                ExpectMsg<FSMBase.Transition<UserState>>(
                    state => { return state.From == UserState.Shopping && state.To == UserState.Inactive; });
                return true;
            });
            ExpectNoMsg(TimeSpan.FromSeconds(0.6));
            recoveredFsmRef.Tell(PoisonPill.Instance);
            ExpectTerminated(recoveredFsmRef);

            recoveredFsmRef = Sys.ActorOf(Props.Create(() => new WebStoreCustomerFSMAsync(Name, dummyReportActorRef)));
            Watch(recoveredFsmRef);
            recoveredFsmRef.Tell(new FSMBase.SubscribeTransitionCallBack(TestActor));
            ExpectMsg<FSMBase.CurrentState<UserState>>(state => state.State == UserState.Inactive);
            ExpectTerminated(recoveredFsmRef);
        }

        [Fact]
        public void PersistentFSM_should_not_trigger_onTransition_for_stay()
        {
            var reportActorProbe = CreateTestProbe(Sys);

            var fsmRef = Sys.ActorOf(Props.Create(() => new SimpleTransitionFSMAsync(Name, reportActorProbe.Ref)));

            reportActorProbe.ExpectNoMsg(TimeSpan.FromSeconds(3));

            fsmRef.Tell("goto(the same state)");

            reportActorProbe.ExpectNoMsg(TimeSpan.FromSeconds(3));

            fsmRef.Tell("stay");

            reportActorProbe.ExpectNoMsg(TimeSpan.FromSeconds(3));
        }


        [Fact]
        public void PersistentFSM_should_not_persist_state_change_event_when_staying_in_the_same_state()
        {
            var dummyReportActorRef = CreateTestProbe().Ref;

            var fsmRef = Sys.ActorOf(Props.Create(()=> new WebStoreCustomerFSMAsync(Name, dummyReportActorRef)));

            Watch(fsmRef);
            fsmRef.Tell(new FSMBase.SubscribeTransitionCallBack(TestActor));

            var shirt = new Item("1", "Shirt", 59.99F);
            var shoes = new Item("2", "Shoes", 89.99F);
            var coat = new Item("3", "Coat", 119.99F);

            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(shirt));
            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(shoes));
            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(coat));
            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new Buy());
            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new Leave());
            ExpectMsg<FSMBase.CurrentState<UserState>>(state => state.State == UserState.LookingAround);
            ExpectMsg<EmptyShoppingCart>();
            ExpectMsg<FSMBase.Transition<UserState>>(state => state.From == UserState.LookingAround);
            ExpectMsg<NonEmptyShoppingCart>();
            ExpectMsg<NonEmptyShoppingCart>();
            ExpectMsg<NonEmptyShoppingCart>();
            ExpectMsg<FSMBase.Transition<UserState>>();
            ExpectMsg<NonEmptyShoppingCart>();
            ExpectTerminated(fsmRef);

            var persistentEventsStreamer = Sys.ActorOf(Props.Create(()=> new PersistentEventsStreamer(Name, TestActor)));


            ExpectMsg<ItemAdded>();
            ExpectMsg<PersistentFSMBaseAsync<UserState, IShoppingCart, IDomainEvent>.StateChangeEvent>();


            ExpectMsg<ItemAdded>();
            ExpectMsg<PersistentFSMBaseAsync<UserState, IShoppingCart, IDomainEvent>.StateChangeEvent>();


            ExpectMsg<ItemAdded>();
            ExpectMsg<PersistentFSMBaseAsync<UserState, IShoppingCart, IDomainEvent>.StateChangeEvent>();


            ExpectMsg<OrderExecuted>();
            ExpectMsg<PersistentFSMBaseAsync<UserState, IShoppingCart, IDomainEvent>.StateChangeEvent>();

            Watch(persistentEventsStreamer);

            persistentEventsStreamer.Tell(PoisonPill.Instance);

            ExpectTerminated(persistentEventsStreamer);
        }


        internal class WebStoreCustomerFSMAsync : PersistentFSMAsync<UserState, IShoppingCart, IDomainEvent>
        {
            private readonly IActorRef _reportActor;
            private readonly string _persistenceId;

            public WebStoreCustomerFSMAsync(string persistenceId, IActorRef reportActor)
            {
                _persistenceId = persistenceId;
                _reportActor = reportActor;
                StartWith(UserState.LookingAround, new EmptyShoppingCart());

                When(UserState.LookingAround, async (@event, state) =>
                {
                    if (@event.FsmEvent is AddItem)
                    {
                        var addItem = (AddItem) @event.FsmEvent;
                        return
                            GoTo(UserState.Shopping)
                                .Applying(new ItemAdded(addItem.Item)).ForMax(TimeSpan.FromSeconds(1));
                    }
                    if (@event.FsmEvent is GetCurrentCart)
                    {
                        return Stay().Replying(@event.StateData);
                    }
                    return state;
                });


                When(UserState.Shopping, async (@event, state) =>
                {
                    if (@event.FsmEvent is AddItem)
                    {
                        var addItem = ((AddItem) @event.FsmEvent);
                        return Stay().Applying(new ItemAdded(addItem.Item)).ForMax(TimeSpan.FromSeconds(1));
                    }
                    if (@event.FsmEvent is Buy)
                    {
                        return
                            GoTo(UserState.Paid)
                                .Applying(new OrderExecuted())
                                .AndThen(cart =>
                                {
                                    if (cart is NonEmptyShoppingCart)
                                    {
                                        _reportActor.Tell(new PurchaseWasMade());
                                    }
                                });
                    }
                    if (@event.FsmEvent is Leave)
                    {
                        return
                            Stop()
                                .Applying(new OrderDiscarded())
                                .AndThen(cart => _reportActor.Tell(new ShoppingCardDiscarded()));
                    }
                    if (@event.FsmEvent is GetCurrentCart)
                    {
                        return Stay().Replying(@event.StateData);
                    }
                    if (@event.FsmEvent is StateTimeout)
                    {
                        return GoTo(UserState.Inactive).ForMax(TimeSpan.FromSeconds(2));
                    }
                    return state;
                });


                When(UserState.Inactive, async (@event, state) =>
                {
                    if (@event.FsmEvent is AddItem)
                    {
                        var addItem = (AddItem) @event.FsmEvent;
                        return
                            GoTo(UserState.Shopping)
                                .Applying(new ItemAdded(addItem.Item))
                                .ForMax(TimeSpan.FromSeconds(1));
                    }
                    if (@event.FsmEvent is StateTimeout)
                    {
                        //var addItem = ((AddItem)@event)
                        return
                            Stop()
                                .Applying(new OrderDiscarded())
                                .AndThen(cart => _reportActor.Tell(new ShoppingCardDiscarded()));
                    }
                    return state;
                });

                When(UserState.Paid, async (@event, state) =>
                {
                    if (@event.FsmEvent is Leave)
                    {
                        return Stop();
                    }
                    if (@event.FsmEvent is GetCurrentCart)
                    {
                        return Stay().Replying(@event.StateData);
                    }
                    return state;
                });
            }

            public override string PersistenceId
            {
                get { return _persistenceId; }
            }


            protected override void OnRecoveryCompleted()
            {
            }

            protected override IShoppingCart ApplyEvent(IDomainEvent e, IShoppingCart data)
            {
                if (e is ItemAdded)
                {
                    var itemAdded = (ItemAdded) e;
                    return data.AddItem(itemAdded.Item);
                }
                if (e is OrderExecuted)
                {
                    return data;
                }
                if (e is OrderDiscarded)
                {
                    return data.Empty();
                }

                return data;
            }
        }

        internal class SimpleTransitionFSMAsync : PersistentFSMAsync<UserState, IShoppingCart, IDomainEvent>
        {
            private readonly IActorRef _reportActor;
            private readonly string _persistenceId;

            public SimpleTransitionFSMAsync(string persistenceId, IActorRef reportActor)
            {
                _persistenceId = persistenceId;
                _reportActor = reportActor;
                StartWith(UserState.LookingAround, new EmptyShoppingCart());

                When(UserState.LookingAround, async (@event, state) =>
                {
                    if ((string)@event.FsmEvent == "stay")
                    {
                        return Stay();
                    }
                    return GoTo(UserState.LookingAround);
                });
                OnTransition((state, nextState) => _reportActor.Tell(string.Format("{0} -> {1}", state, nextState)));
            }

            public override string PersistenceId
            {
                get { return _persistenceId; }
            }


            protected override void OnRecoveryCompleted()
            {
            }

            protected override IShoppingCart ApplyEvent(IDomainEvent e, IShoppingCart data)
            {
                return data;
            }
        }
    }
}