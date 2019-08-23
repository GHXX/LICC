﻿using LICC.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace LICC
{
    public interface ICommandRegistry
    {
        void RegisterCommandsInExecutingAssembly();
        void RegisterCommandsIn(Assembly assembly);
        void RegisterCommandsIn(Type type);
        void RegisterCommand(MethodInfo method);
    }

    public class CommandRegistry : ICommandRegistry
    {
        internal struct Command
        {
            public string Name { get; }
            public string Usage { get; }
            public (string Name, Type Type)[] Params { get; }

            private readonly Action<object[]> Caller;

            public Command(string name, string usage, MethodInfo method)
            {
                this.Name = name;
                this.Usage = usage;
                this.Params = method.GetParameters().Select(o => (o.Name, o.ParameterType)).ToArray();

                var paramsParam = Expression.Parameter(typeof(object[]), "params");
                Caller = Expression.Lambda<Action<object[]>>(
                    Expression.Call(
                        method,
                        method.GetParameters().Select((o, i) =>
                                            Expression.Convert(
                                                Expression.ArrayAccess(paramsParam, Expression.Constant(i)),
                                                o.ParameterType)))).Compile();
            }
        }

        private readonly IDictionary<string, Command> Commands = new Dictionary<string, Command>();

        internal CommandRegistry()
        {
        }

        private void RegisterCommand(MethodInfo method, bool ignoreInvalid)
        {
            var attr = method.GetCustomAttribute<CommandAttribute>();

            if (attr == null)
            {
                if (ignoreInvalid)
                    return;
                else
                    throw new InvalidCommandMethodException("Command methods must be decorated with the [Command] attribute");
            }

            if (Commands.ContainsKey(attr.Name))
            {
                if (ignoreInvalid)
                    return;
                else
                    throw new InvalidCommandMethodException("That command name is already in use");
            }

            Commands.Add(attr.Name, new Command(attr.Name, attr.Usage, method));
        }

        public void RegisterCommand(MethodInfo method) => RegisterCommand(method, false);

        public void RegisterCommandsIn(Type type)
        {
            foreach (var item in type.GetMethods(BindingFlags.Static))
            {
                RegisterCommand(item, true);
            }
        }

        public void RegisterCommandsIn(Assembly assembly)
        {
            foreach (var item in assembly.GetTypes())
            {
                RegisterCommandsIn(item);
            }
        }

        public void RegisterCommandsInExecutingAssembly() => RegisterCommandsIn(Assembly.GetExecutingAssembly());
    }
}
