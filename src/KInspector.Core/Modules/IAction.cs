﻿using KInspector.Core.Models;

namespace KInspector.Core.Modules
{
    public interface IAction : IModule
    {
        ActionResults Execute(string OptionsJson);
    }
}