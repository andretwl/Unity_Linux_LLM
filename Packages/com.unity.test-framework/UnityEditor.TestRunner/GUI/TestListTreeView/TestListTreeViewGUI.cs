using TreeViewController = UnityEditor.IMGUI.Controls.TreeViewController<int>;
using TreeViewGUI = UnityEditor.IMGUI.Controls.TreeViewGUI<int>;


namespace UnityEditor.TestTools.TestRunner.GUI
{
    internal class TestListTreeViewGUI : TreeViewGUI
    {
        public TestListTreeViewGUI(TreeViewController testListTree) : base(testListTree)
        {
        }
    }
}
