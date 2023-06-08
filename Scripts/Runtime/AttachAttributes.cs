using System;
using UnityEngine;

namespace Nrjwolf.Tools.AttachAttributes
{
    [AttributeUsage(System.AttributeTargets.Field)] public class GetComponentAttribute : AttachPropertyAttribute { }

    [AttributeUsage(System.AttributeTargets.Field)]
    public class GetComponentInChildrenAttribute : AttachPropertyAttribute
    {
        public bool IncludeInactive { get; private set; }
        public string ChildName;

        public GetComponentInChildrenAttribute(bool includeInactive = false)
        {
            IncludeInactive = includeInactive;
        }

        public GetComponentInChildrenAttribute(string childName)
        {
            ChildName = childName;
        }
    }

    [AttributeUsage(System.AttributeTargets.Field)] public class AddComponentAttribute : AttachPropertyAttribute { }
    [AttributeUsage(System.AttributeTargets.Field)] public class FindObjectOfTypeAttribute : AttachPropertyAttribute { }
    [AttributeUsage(System.AttributeTargets.Field)] public class GetComponentInParent : AttachPropertyAttribute { }

    [AttributeUsage(System.AttributeTargets.Field)]
    public class CustomFetchAttribute : BaseCustomFetchAttribute
    {
        public CustomFetchAttribute(string funcName) : base(funcName) { }
    }

    public abstract class BaseCustomFetchAttribute : AttachPropertyAttribute
    {
        public string CustomFuncName;

        protected BaseCustomFetchAttribute(string funcName)
        {
            CustomFuncName = funcName;
        }
    }

    public class AttachPropertyAttribute : PropertyAttribute { }
}
