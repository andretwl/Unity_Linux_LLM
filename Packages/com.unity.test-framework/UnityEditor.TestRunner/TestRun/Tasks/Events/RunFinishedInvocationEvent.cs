using System.Collections;

namespace UnityEditor.TestTools.TestRunner.TestRun.Tasks.Events
{
    internal class RunFinishedInvocationEvent : TestTaskBase
    {
        public override IEnumerator Execute(TestJobData testJobData)
        {
            if (testJobData.TestResults == null)
            {
                // Temporary workaround to ensure that we do not loose the non serializable results due to a test leaking a domain reload.
                testJobData.TestResults = testJobData.editModeRunner.m_Runner.Result;
            }
            testJobData.editModeRunner.Dispose();

            testJobData.RunFinishedEvent.Invoke(testJobData.TestResults);
            yield break;
        }
    }
}
