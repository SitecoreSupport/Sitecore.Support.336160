extern alias local;
using Sitecore.Analytics;
using Sitecore.Analytics.Automation;
using Sitecore.Analytics.Automation.Data;
using Sitecore.Analytics.Automation.MarketingAutomation;
using Sitecore.Analytics.DataAccess;
using Sitecore.Analytics.Tracking;
using Sitecore.Configuration;
using Sitecore.Diagnostics;
using Sitecore.Xdb.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sitecore.Support.Analytics.Automation
{
    public class AutomationWorker : Sitecore.Analytics.Automation.AutomationWorker
    {
        ContactManager contactManager = Factory.CreateObject("tracking/contactManager", true) as ContactManager;
        public override bool Process()
        {
            if (!XdbSettings.Enabled)
            {
                Log.Info("AutomationWorker was not processed as xDB is disabled.", this);
                return false;
            }
            Assert.IsNotNull(this.ContactRepository, "AutomationWorker/ContactRepository configuration does not exist");
            IEnumerable<Guid> automationStateKeys = this.GetAutomationStateKeys();
            if (automationStateKeys == null)
            {
                Log.Error("AutomationStateKeys is null", this);
                return false;
            }
            foreach (Guid guid in automationStateKeys)
            {
                LockAttemptResult<Contact> result = contactManager.TryLoadContact(guid, 1);
                if ((result.Status == LockAttemptStatus.Success) && (result.Object != null))
                {
                    try
                    {
                        Func<AutomationStateContext, bool> predicate = null;
                        DateTime now = DateTime.UtcNow;
                        StandardSession session = new StandardSession(result.Object);
                        using (new TrackerSwitcher(new local.Sitecore.Analytics.NullTracker(session)))
                        {
                            Tracker.IsActive = true;
                            using (new SessionSwitcher(session))
                            {
                                if (predicate == null)
                                {
                                    predicate = delegate (AutomationStateContext state)
                                    {
                                        if (state == null)
                                        {
                                            return false;
                                        }
                                        DateTime? wakeUpDateTime = state.WakeUpDateTime;
                                        DateTime time1 = now;
                                        if (!wakeUpDateTime.HasValue)
                                        {
                                            return false;
                                        }
                                        return wakeUpDateTime.GetValueOrDefault() <= time1;
                                    };
                                }
                                foreach (AutomationStateContext context in session.CreateAutomationStateManager().GetAutomationStates().Where<AutomationStateContext>(predicate))
                                {
                                    if (typeof(AutomationStateContext).GetProperty("IsDue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) != null)
                                    {
                                        typeof(AutomationStateContext).GetProperty("IsDue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public).SetValue(context, true);
                                    }
                                    else
                                    {
                                        Log.Error("IsDue property null for AutomationStateContext", this);
                                    }

                                    AutomationUpdater.BackgroundProcess(context);
                                }
                                if (session.Contact != null)
                                {
                                    contactManager.SaveAndReleaseContactToXdb(session.Contact);
                                }
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        Log.Error($"Cannot process EAS record of contact '{guid}' by due time", exception, this);
                        contactManager.ReleaseContact(guid);
                    }
                }
            }
            return true;
        }

        private IEnumerable<Guid> GetAutomationStateKeys() =>
    AutomationManager.Provider.GetContactIdsToProcessByWakeUpTime();


    }
}