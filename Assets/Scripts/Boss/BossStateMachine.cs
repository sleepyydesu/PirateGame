using System;
using System.Collections.Generic;

namespace PirateGame.Enemies
{
    // A real hierarchy: common combat rules execute before the active combat leaf.
    // Transitions exit only to the lowest common ancestor, preserving parent context.
    internal sealed class BossStateMachine
    {
        internal sealed class Node
        {
            public readonly string Name;
            public readonly Node Parent;
            public Action Enter, Exit;
            public Func<bool> Tick; // false consumes the tick (e.g. parent transitioned).
            public Node(string name, Node parent = null) { Name = name; Parent = parent; }
        }
        public Node Current { get; private set; }
        public bool IsWithin(Node parent)
        {
            for (Node n = Current; n != null; n = n.Parent) if (n == parent) return true;
            return false;
        }
        public void Change(Node next)
        {
            if (next == Current) return;
            Node common = Current;
            while (common != null && !AncestorOf(common, next)) common = common.Parent;
            for (Node n = Current; n != common; n = n.Parent) n.Exit?.Invoke();
            var entering = new Stack<Node>();
            for (Node n = next; n != common; n = n.Parent) entering.Push(n);
            Current = next;
            while (entering.Count > 0) entering.Pop().Enter?.Invoke();
        }
        public void Tick()
        {
            var path = new Stack<Node>();
            Node initial = Current;
            for (Node n = initial; n != null; n = n.Parent) path.Push(n);
            while (path.Count > 0)
            {
                if (path.Pop().Tick?.Invoke() == false || Current != initial) return;
            }
        }
        private static bool AncestorOf(Node ancestor, Node node)
        {
            for (; node != null; node = node.Parent) if (node == ancestor) return true;
            return false;
        }
    }
}
