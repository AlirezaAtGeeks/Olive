﻿using System;

namespace Olive.Mvc
{
    /// <summary>
    /// Intended for use on methods to specify the primary type of the result that is returned in happy scenarios.
    /// This is used in cases where the actual return type of the method itself can't be that type and has to be a generic interface to cater for exceptional scenarios (e.g. Web Api methods return IActionResult).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ReturnsAttribute : Attribute
    {
        public readonly Type ReturnType;
        public ReturnsAttribute(Type returnType)
            => ReturnType = returnType ?? throw new ArgumentNullException();

        /// <summary>
        /// When set, if the method takes a single Guid parameter then Olive.ApiProxy will generate
        /// a data provider that will be registered in consumer app, so this Api method can be invoked
        /// also using Database.Get().
        /// </summary>
        public bool EnableDatabaseGet { get; set; } = true;
    }
}