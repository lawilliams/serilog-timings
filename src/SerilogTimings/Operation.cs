﻿// Copyright 2016 SerilogTimings Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Diagnostics;
using System.Linq;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using SerilogTimings.Extensions;

namespace SerilogTimings
{
    /// <summary>
    /// Records operation timings to the Serilog log.
    /// </summary>
    /// <remarks>
    /// Static members on this class are thread-safe. Instances
    /// of <see cref="Operation"/> are designed for use on a single thread only.
    /// </remarks>
    public class Operation : IDisposable
    {
        /// <summary>
        /// Property names attached to events by <see cref="Operation"/>s.
        /// </summary>
        public enum Properties
        {
            /// <summary>
            /// The timing, in milliseconds.
            /// </summary>
            Elapsed,

            /// <summary>
            /// Completion status, either <em>completed</em> or <em>discarded</em>.
            /// </summary>
            Outcome,

            /// <summary>
            /// A unique identifier added to the log context during
            /// the operation.
            /// </summary>
            OperationId
        };

        const string OutcomeCompleted = "completed", OutcomeAbandoned = "abandoned";

        readonly ILogger _target;
        readonly string _messageTemplate;
        readonly object[] _args;
        readonly Stopwatch _stopwatch;

        IDisposable _popContext;
        CompletionBehaviour _completionBehaviour;

        internal Operation(ILogger target, string messageTemplate, object[] args, CompletionBehaviour completionBehaviour)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (messageTemplate == null) throw new ArgumentNullException(nameof(messageTemplate));
            if (args == null) throw new ArgumentNullException(nameof(args));
            _target = target;
            _messageTemplate = messageTemplate;
            _args = args;
            _completionBehaviour = completionBehaviour;
            _popContext = LogContext.PushProperty(nameof(Properties.OperationId), Guid.NewGuid());
            _stopwatch = Stopwatch.StartNew();
        }

        public static Operation Begin(string messageTemplate, params object[] args)
        {
            return Log.Logger.BeginOperation(messageTemplate, args);
        }

        public static IDisposable Time(string messageTemplate, params object[] args)
        {
            return Log.Logger.TimeOperation(messageTemplate, args);
        }

        public void Complete()
        {
            if (_completionBehaviour == CompletionBehaviour.Silent)
                return;

            Write(_target, LogEventLevel.Information, OutcomeCompleted);
        }

        public void Complete(string resultPropertyName, object result, bool destructureObjects = false)
        {
            if (resultPropertyName == null) throw new ArgumentNullException(nameof(resultPropertyName));

            if (_completionBehaviour == CompletionBehaviour.Silent)
                return;

            Write(_target.ForContext(resultPropertyName, result, destructureObjects), LogEventLevel.Information, OutcomeCompleted);
        }

        public void Cancel()
        {
            _completionBehaviour = CompletionBehaviour.Silent;
            PopLogContext();
        }

        public void Dispose()
        {
            switch (_completionBehaviour)
            {
                case CompletionBehaviour.Silent:
                    break;

                case CompletionBehaviour.Abandon:
                    Write(_target, LogEventLevel.Warning, OutcomeAbandoned);
                    break;

                case CompletionBehaviour.Complete:
                    Write(_target, LogEventLevel.Information, OutcomeCompleted);
                    break;

                default:
                    throw new InvalidOperationException("Unknown underlying state value");
            }

            PopLogContext();
        }

        void PopLogContext()
        {
            _popContext?.Dispose();
            _popContext = null;
        }

        void Write(ILogger target, LogEventLevel level, string outcome)
        {
            _completionBehaviour = CompletionBehaviour.Silent;

            var elapsed = _stopwatch.Elapsed.TotalMilliseconds;

            target
                .ForContext<Operation>()
                .Write(level, $"{_messageTemplate} {{{nameof(Properties.Outcome)}}} in {{{nameof(Properties.Elapsed)}:0.0}} ms", _args.Concat(new object[] {outcome, elapsed }).ToArray());

            PopLogContext();
        }
    }
}
