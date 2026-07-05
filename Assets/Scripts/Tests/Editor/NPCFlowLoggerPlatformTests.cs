using NUnit.Framework;
using UnityEngine;

namespace NPCSystem.Tests
{
    public class NPCFlowLoggerPlatformTests
    {
        [Test]
        public void SupportsPersistentFileLogging_DisablesWebGL()
        {
            Assert.That(NPCFlowLogger.SupportsPersistentFileLogging(RuntimePlatform.WebGLPlayer), Is.False);
        }

        [Test]
        public void SupportsPersistentFileLogging_AllowsDesktopEditor()
        {
            Assert.That(NPCFlowLogger.SupportsPersistentFileLogging(RuntimePlatform.LinuxEditor), Is.True);
        }
    }
}
