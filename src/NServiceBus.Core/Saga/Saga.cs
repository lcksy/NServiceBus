namespace NServiceBus.Saga
{
    using System;
    using System.Linq.Expressions;

    /// <summary>
    /// This class is used to define sagas containing data and handling a message.
    /// To handle more message types, implement <see cref="IHandleMessages{T}"/>
    /// for the relevant types.
    /// To signify that the receipt of a message should start this saga,
    /// implement <see cref="IAmStartedByMessages{T}"/> for the relevant message type.
    /// </summary>
    public abstract class Saga 
    {
        /// <summary>
        /// The saga's typed data.
        /// </summary>
        public IContainSagaData Entity{get; set; }

        bool configuring;
        internal void Configure()
        {
            configuring = true;
            ConfigureHowToFindSaga();
            configuring = false;
        }

        /// <summary>
        /// Override this method in order to configure how this saga's data should be found.
        /// Call <see cref="ConfigureMapping{TSagaData,TMessage}"/> or <see cref="Saga{T}.ConfigureMapping{TMessage}"/>for each property of each message you want
        /// to use for lookup.
        /// </summary>
        public virtual void ConfigureHowToFindSaga()
        {
        }


        /// <summary>
        /// When the infrastructure is handling a message of the given type
        /// this specifies which message property should be matched to 
        /// which saga entity property in the persistent saga store.
        /// </summary>
        protected virtual ToSagaExpression<TSagaData, TMessage> ConfigureMapping<TSagaData,TMessage>(Expression<Func<TMessage, object>> messageProperty) where TSagaData : IContainSagaData
        {
            if (!configuring)
                throw new InvalidOperationException("Cannot configure mappings outside of 'ConfigureHowToFindSaga'.");

            return new ToSagaExpression<TSagaData, TMessage>(SagaMessageFindingConfiguration, messageProperty);
        }

        /// <summary>
        /// Bus object used for retrieving the sender endpoint which caused this saga to start.
        /// Necessary for <see cref="ReplyToOriginator" />.
        /// </summary>
        public IBus Bus
        {
            get
            {
                if (bus == null)
                    throw new InvalidOperationException("No IBus instance available, please configure one and also verify that you're not defining your own Bus property in your saga since that hides the one in the base class");

                return bus;
            }

            set { bus = value; }
        }

        IBus bus;

        /// <summary>
        /// Object used to configure mapping between saga properties and message properties
        /// for the purposes of finding sagas when a message arrives.
        /// 
        /// Do NOT use at runtime (handling messages) - it will be null.
        /// </summary>
        public IConfigureHowToFindSagaWithMessage SagaMessageFindingConfiguration { get; set; }

        /// <summary>
        /// Indicates that the saga is complete.
        /// In order to set this value, use the <see cref="MarkAsComplete" /> method.
        /// </summary>
        public bool Completed { get; private set; }

        /// <summary>
        /// Request for a timeout to occur at the given <see cref="DateTime"/>.
        /// </summary>
        /// <param name="at"><see cref="DateTime"/> to send timeout <typeparamref name="TTimeoutMessageType"/>.</param>
        protected void RequestTimeout<TTimeoutMessageType>(DateTime at) where TTimeoutMessageType : new()
        {
            RequestTimeout(at, Activator.CreateInstance<TTimeoutMessageType>());
        }

        /// <summary>
        /// Request for a timeout to occur at the given <see cref="DateTime"/>.
        /// </summary>
        /// <param name="at"><see cref="DateTime"/> to send call <paramref name="action"/>.</param>
        /// <param name="action">Callback to execute after <paramref name="at"/> is reached.</param>
        protected void RequestTimeout<TTimeoutMessageType>(DateTime at, Action<TTimeoutMessageType> action) where TTimeoutMessageType : new()
        {
            var instance = Activator.CreateInstance<TTimeoutMessageType>();
            action(instance);
            RequestTimeout(at, instance);
        }

        /// <summary>
        /// Request for a timeout to occur at the given <see cref="DateTime"/>.
        /// </summary>
        /// <param name="at"><see cref="DateTime"/> to send timeout <paramref name="timeoutMessage"/>.</param>
        /// <param name="timeoutMessage">The message to send after <paramref name="at"/> is reached.</param>
        protected void RequestTimeout<TTimeoutMessageType>(DateTime at, TTimeoutMessageType timeoutMessage) where TTimeoutMessageType : new()
        {
            if (at.Kind == DateTimeKind.Unspecified)
            {
                throw new InvalidOperationException("Kind property of DateTime 'at' must be specified.");
            }

            VerifySagaCanHandleTimeout(timeoutMessage);
            SetTimeoutHeaders(timeoutMessage);

            Bus.Defer(at, timeoutMessage);
        }

        void VerifySagaCanHandleTimeout<TTimeoutMessageType>(TTimeoutMessageType timeoutMessage) where TTimeoutMessageType : new()
        {
            var canHandleTimeoutMessage = this is IHandleTimeouts<TTimeoutMessageType>;
            if (!canHandleTimeoutMessage)
            {
                var message = string.Format("The type '{0}' cannot request timeouts for '{1}' because it does not implement 'IHandleTimeouts<{2}>'", GetType().Name, timeoutMessage, typeof(TTimeoutMessageType).Name);
                throw new Exception(message);
            }
        }

        /// <summary>
        /// Request for a timeout to occur within the give <see cref="TimeSpan"/>.
        /// </summary>
        /// <param name="within">Given <see cref="TimeSpan"/> to delay timeout message by.</param>
        protected void RequestTimeout<TTimeoutMessageType>(TimeSpan within) where TTimeoutMessageType : new()
        {
            RequestTimeout(within, Activator.CreateInstance<TTimeoutMessageType>());
        }

        /// <summary>
        /// Request for a timeout to occur within the give <see cref="TimeSpan"/>.
        /// </summary>
        /// <param name="within">Given <see cref="TimeSpan"/> to delay timeout message by.</param>
        /// <param name="messageConstructor">An <see cref="Action"/> which initializes properties of the message that is sent after <paramref name="within"/> expires.</param>
        protected void RequestTimeout<TTimeoutMessageType>(TimeSpan within, Action<TTimeoutMessageType> messageConstructor) where TTimeoutMessageType : new()
        {
            var instance = Activator.CreateInstance<TTimeoutMessageType>();
            messageConstructor(instance);
            RequestTimeout(within, instance);
        }

        /// <summary>
        /// Request for a timeout to occur within the given <see cref="TimeSpan"/>.
        /// </summary>
        /// <param name="within">Given <see cref="TimeSpan"/> to delay timeout message by.</param>
        /// <param name="timeoutMessage">The message to send after <paramref name="within"/> expires.</param>
        protected void RequestTimeout<TTimeoutMessageType>(TimeSpan within, TTimeoutMessageType timeoutMessage) where TTimeoutMessageType : new()
        {
            VerifySagaCanHandleTimeout(timeoutMessage);
            SetTimeoutHeaders(timeoutMessage);

            Bus.Defer(within, timeoutMessage);
        }


        void SetTimeoutHeaders(object toSend)
        {
            Headers.SetMessageHeader(toSend, Headers.SagaId, Entity.Id.ToString());
            Headers.SetMessageHeader(toSend, Headers.IsSagaTimeoutMessage, Boolean.TrueString);
            Headers.SetMessageHeader(toSend, Headers.SagaType, GetType().AssemblyQualifiedName);
        }

        /// <summary>
        /// Sends the <paramref name="message"/> using the bus to the endpoint that caused this saga to start.
        /// </summary>
        protected virtual void ReplyToOriginator(object message)
        {
            if (string.IsNullOrEmpty(Entity.Originator))
            {
                throw new Exception("Entity.Originator cannot be null. Perhaps the sender is a SendOnly endpoint.");
            }
            Bus.Send(Entity.Originator, Entity.OriginalMessageId, message);
        }

        /// <summary>
        /// Instantiates a message of the given type, setting its properties using the given action,
        /// and sends it using the bus to the endpoint that caused this saga to start.
        /// </summary>
        /// <typeparam name="TMessage">The type of message to construct.</typeparam>
        /// <param name="messageConstructor">An <see cref="Action"/> which initializes properties of the message reply with.</param>
        protected virtual void ReplyToOriginator<TMessage>(Action<TMessage> messageConstructor) where TMessage : new()
        {
            if (messageConstructor != null)
            {
                var instance = Activator.CreateInstance<TMessage>();
                messageConstructor(instance);
                ReplyToOriginator(instance);
            }
            else
            {
                ReplyToOriginator(null);
            }
        }

        /// <summary>
        /// Marks the saga as complete.
        /// This may result in the sagas state being deleted by the persister.
        /// </summary>
        protected virtual void MarkAsComplete()
        {
            Completed = true;
        }

    }
}
