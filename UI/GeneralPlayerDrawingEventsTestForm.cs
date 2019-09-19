﻿using GTI.Modules.Shared;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GameTech.Elite.Base;
using GameTech.Elite.Client;
using System.Globalization;
using GTI.Modules.PlayerCenter.Properties;
using System.Threading;

namespace GTI.Modules.PlayerCenter.UI
{
    public partial class GeneralPlayerDrawingEventsTestForm : GradientForm
    {
        private List<GeneralPlayerDrawing> m_drawings;
        private string m_displayText;
        protected WaitForm m_waitForm;
        protected BackgroundWorker m_worker;
        protected bool m_serverCommFailed;
   

        public GeneralPlayerDrawingEventsTestForm(List<GeneralPlayerDrawing> drawings, string displayText)
        {
            InitializeComponent();
            m_drawings = drawings ?? new List<GeneralPlayerDrawing>();
            m_displayText = displayText;
            LoadCurrentAndRecentDrawingEvents(false, false);
            SetBtnControlDisable(false);
            PopulateRaffleAvailability();
            cmbxAvailableRaffles.SelectedIndex = 0;
            AppliedSystemSettingDisplayedText();                       
        }

        private void PopulateRaffleAvailability()
        {
            cmbxAvailableRaffles.Items.Clear();
            cmbxAvailableRaffles.Items.Add("Current " + m_displayText +"s");
            cmbxAvailableRaffles.Items.Add("Completed " + m_displayText + "s");
            cmbxAvailableRaffles.Items.Add("Cancelled " + m_displayText + "s");
            cmbxAvailableRaffles.Items.Add("All " + m_displayText + "s");
        }

        public void SelectNone()
        {
            SetBtnControlDisable(false);
            drawingEventsLV.SelectedItems.Clear();
     
        }

        private void GenerateCurrentDrawing()
        {
            StringBuilder sb = new StringBuilder();
            var gResult = GenerateGeneralDrawingsEventsMessage.GenerateDrawingEvents(DateTime.Now.Date);            
            LoadCurrentAndRecentDrawingEvents(false, false);         
        }

        private void AppliedSystemSettingDisplayedText()
        {
            imgbtnCancel.Text = CultureInfo.CurrentCulture.TextInfo.ToTitleCase("Cancel " + m_displayText.ToLower());
            imgbtnExecute.Text = CultureInfo.CurrentCulture.TextInfo.ToTitleCase("Run " + m_displayText.ToLower());
            imgbtnReinstate.Text = CultureInfo.CurrentCulture.TextInfo.ToTitleCase("Reinstate " + m_displayText.ToLower());
        }

        private void SetBtnControlDisable(bool set)
        {
            imgbtnViewEntriesResult.Enabled = set;
            imgbtnReinstate.Enabled = set;
            imgbtnExecute.Enabled = set;
            imgbtnCancel.Enabled = set;
        }

        private void LoadCurrentAndRecentDrawingEvents(bool includeEntries, bool includeResults)
        {
            GeneralPlayerDrawingEvent prevSel = null;
            ListViewItem newSelLVI = null;

            if (drawingEventsLV.SelectedItems.Count == 1)
            {
                prevSel = drawingEventsLV.SelectedItems[0].Tag as GeneralPlayerDrawingEvent;
            }

            DataGridView dgvtest = new DataGridView();
            DataGridViewRow  dgvr = new DataGridViewRow();
            drawingEventsLV.Items.Clear();
            drawingEventsLV.Columns.Clear();
            drawingEventsLV.BeginUpdate();

            try
            {
                var drawingEvents = GetGeneralDrawingEventsMessage.GetEvents(0, 0, DateTime.Now.Date.AddDays(-14), DateTime.Now.Date, includeEntries, includeResults);
                
                if(drawingEvents.Count == 0)
                {
                    drawingEventsLV.Columns.Add("");
                    drawingEventsLV.Items.Add("No " + m_displayText + " Events Found");
                    drawingEventsLV.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
                    SetBtnControlDisable(false);//DE14454
                }
                else
                {
                    drawingEventsLV.Columns.Add(m_displayText);
                    drawingEventsLV.Columns.Add("Entries Begin");
                    drawingEventsLV.Columns.Add("Entries End");
                    drawingEventsLV.Columns.Add("Schedule For");
                    drawingEventsLV.Columns.Add("Held On");
                    drawingEventsLV.Columns.Add("Cancelled On");
                    //drawingEventsLV.Columns.Add("Players");
                    //drawingEventsLV.Columns.Add("Entries");
                    //drawingEventsLV.Columns.Add("# Drawn");

                    bool heldPresent = false
                        , cancellationPresent = false;



                    foreach(GeneralPlayerDrawingEvent de in drawingEvents)
                    {
                        GeneralPlayerDrawing ed = m_drawings.FirstOrDefault((d) => d.Id == de.DrawingId);
                        var playersEntered = (from e in de.Entries
                                              select e.PlayerId).Distinct();
                        var totalEntries = (from e in de.Entries
                                            select e.EntryCount).Sum();

                        //default (true) show drawing that are currently available to run without any issues.
                        //Removed drawing that are already held, cancelled and if the minimum entries is greater than the current total entries.                   
                        //if (chkbx_showAvailableDrawing.Checked)
                        //{
                        //    if (de.HeldWhen.HasValue || de.CancelledWhen.HasValue)
                        //    {
                        //        continue;
                        //    }
                        //    else                             
                        //    if (ed.MinimumEntries > totalEntries && de.EntryPeriodEnd.Date.Subtract(DateTime.Now.Date).Days < 0)
                        //    {
                        //        if (de.ScheduledForWhen == null || de.ScheduledForWhen.Value.Date.Subtract(DateTime.Now.Date).Days < 0)
                        //        {
                        //            continue;
                        //        }
                        //    }
                        //}

                        if (cmbxAvailableRaffles.SelectedIndex == 0)//Current
                        {
                            if (de.HeldWhen.HasValue || de.CancelledWhen.HasValue)
                            {
                                continue;
                            }
                            else
                            if (ed.MinimumEntries > totalEntries && de.EntryPeriodEnd.Date.Subtract(DateTime.Now.Date).Days < 0)
                            {
                                if (de.ScheduledForWhen == null || de.ScheduledForWhen.Value.Date.Subtract(DateTime.Now.Date).Days < 0)
                                {
                                    continue;
                                }
                            }
                        }  
                        else
                        if (cmbxAvailableRaffles.SelectedIndex == 1)
                        {
                            if (!de.HeldWhen.HasValue)
                            {
                                continue;
                            }
                            
                        }
                        else
                        if (cmbxAvailableRaffles.SelectedIndex == 2)
                        {
                            if (!de.CancelledWhen.HasValue)
                            {
                                continue;
                            }
                        }

                        var lvi = drawingEventsLV.Items.Add(ed == null ? String.Format("[{0}]", de.DrawingId) : ed.Name);
                        lvi.Font = new Font(lvi.Font, FontStyle.Regular);
                        //lvi.SubItems.Add(ed == null ? String.Format("[{0}]", de.DrawingId) : ed.Name);
                        lvi.SubItems.Add(de.EntryPeriodBegin.ToShortDateString());
                        lvi.SubItems.Add(de.EntryPeriodEnd.ToShortDateString());
                        lvi.SubItems.Add(de.ScheduledForWhen.HasValue ? de.ScheduledForWhen.Value.ToShortDateString() : "(unspecified)");

                        heldPresent = heldPresent || de.HeldWhen.HasValue;
                        lvi.SubItems.Add(de.HeldWhen.HasValue
                            ? String.Format("{0} {1}", de.HeldWhen.Value.ToShortDateString(), de.HeldWhen.Value.ToShortTimeString())
                            : "never"
                            );

                        cancellationPresent = cancellationPresent || de.CancelledWhen.HasValue;
                        lvi.SubItems.Add(de.CancelledWhen.HasValue
                            ? String.Format("{0} {1}", de.CancelledWhen.Value.ToShortDateString(), de.CancelledWhen.Value.ToShortTimeString())
                            : "---"
                            );

                        //lvi.SubItems.Add(playersEntered.Count().ToString());
                        //lvi.SubItems.Add(totalEntries.ToString());
                        //lvi.SubItems.Add(de.Results.Count.ToString());

                        lvi.Tag = de;

                        if(prevSel != null && de.EventId == prevSel.EventId)
                            newSelLVI = lvi;
                    }

                    if(newSelLVI != null)
                        newSelLVI.Selected = true;

                    drawingEventsLV.AutoResizeColumns(ColumnHeaderAutoResizeStyle.HeaderSize);
                    if (heldPresent)
                        drawingEventsLV.AutoResizeColumn(4, ColumnHeaderAutoResizeStyle.ColumnContent);
                    if (cancellationPresent)
                        drawingEventsLV.AutoResizeColumn(5, ColumnHeaderAutoResizeStyle.ColumnContent);
                }
            }
            finally
            {
                drawingEventsLV.EndUpdate();
            }
        }

        private static string EventsToString(List<GeneralPlayerDrawingEvent> drawingEvents, List<GeneralPlayerDrawing> drawings = null)
        {
            StringBuilder sb = new StringBuilder();
            if(drawingEvents == null || drawingEvents.Count == 0)
                return null;
            foreach(var de in drawingEvents)
            {
                GeneralPlayerDrawing ed = null;
                if(drawings != null)
                    ed = drawings.FirstOrDefault((d) => d.Id == de.DrawingId);

                var dName = ed == null ? String.Format("[{0}]", de.DrawingId) : ("'" + ed.Name + "'");
                sb.AppendFormat("Drawing {0}, Event Id {1}", dName, de.EventId);
                sb.AppendLine();

                if(de.EntryPeriodBegin.ToShortDateString() == de.EntryPeriodEnd.ToShortDateString())
                    sb.AppendFormat("- entry period on {0}", de.EntryPeriodBegin.ToShortDateString());
                else
                    sb.AppendFormat("- entry period from {0} to {1}", de.EntryPeriodBegin.ToShortDateString(), de.EntryPeriodEnd.ToShortDateString());
                sb.AppendLine();

                if(de.ScheduledForWhen.HasValue)
                    sb.AppendFormat("- scheduled to be held {0}.", de.ScheduledForWhen.Value.ToShortDateString());
                else
                    sb.Append("- scheduled data unspecified.");

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private void drawingEventsLV_SelectedIndexChanged(object sender, EventArgs e)
        {
            var selectionMade = drawingEventsLV.SelectedItems.Count == 1;
          
            //eventActionsFLP.Enabled = selectionMade;
            SetBtnControlDisable(selectionMade);

            if (selectionMade)
            {
                var drawingEvent = drawingEventsLV.SelectedItems[0].Tag as GeneralPlayerDrawingEvent;
                if (drawingEvent != null)
                {
                    drawingEventsLV.HideSelection = false;
                    if (!drawingEvent.CancelledWhen.HasValue)//If drawing is not cancelled 
                    {
                        imgbtnReinstate.Enabled = false;
                        if (drawingEvent.HeldWhen.HasValue)
                        {
                            imgbtnCancel.Enabled = false;
                        }
                    }
                    else
                    {
                        imgbtnCancel.Enabled = false;
                    }
                }
                else
                {
                    SetBtnControlDisable(false);
                }
            }
        }

        private void generateCurrentEventsBtn_Click(object sender, EventArgs e)
        {
            StringBuilder sb = new StringBuilder();
            var gResult = GenerateGeneralDrawingsEventsMessage.GenerateDrawingEvents(DateTime.Now.Date);

            var msg = EventsToString(gResult, m_drawings);
            var dr = MessageForm.Show((msg ?? "No Events Generated") + Environment.NewLine + Environment.NewLine + "Reload Recent?", "Generated Events", MessageFormTypes.YesNo);
            if(dr == System.Windows.Forms.DialogResult.Yes)
                LoadCurrentAndRecentDrawingEvents(false, false);
        }

        private void refreshEventsListBtn_Click(object sender, EventArgs e)
        {
            GenerateCurrentDrawing();
            SetBtnControlDisable(false);
        }

        private void startexecuteEventDrawing(object sender, DoWorkEventArgs e)
        {
            int eventId = (int)e.Argument;
            var eeResult = ExecuteGeneralDrawingEventMessage.ExecuteEvent(eventId, true, true);
            e.Result = eeResult;       
        }

        private void executeEventDrawingComplete(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error == null)
            {
                Tuple<bool, GeneralPlayerDrawingEvent> eeResult = (Tuple<bool, GeneralPlayerDrawingEvent>)e.Result;
                bool showEvent = false;

                LoadCurrentAndRecentDrawingEvents(false, false);
                if (!eeResult.Item1)
                {
                    String msg = String.Empty;
                    GeneralPlayerDrawing ed = m_drawings.FirstOrDefault((d) => d.Id == eeResult.Item2.DrawingId);
                    var minEntryRequired = ed.MinimumEntries;

                    if (eeResult.Item2.HeldWhen.HasValue)
                        msg = "Event was already held.";
                    else if (eeResult.Item2.CancelledWhen.HasValue)
                        msg = "Cannot hold a cancelled event.";
                    else if (minEntryRequired > eeResult.Item2.Entries.Count)
                        msg = "Cannot run the " + m_displayText + "."
                    + Environment.NewLine + "The required number of entries has not been met.";
                    else msg = "Event not executed.";

                    var dr = MessageForm.Show(msg, "Run " + m_displayText, MessageFormTypes.OK);
                    showEvent = (dr == System.Windows.Forms.DialogResult.Yes);
                }
                else
                    showEvent = true;

                if (showEvent)
                {
                    var ed = m_drawings.FirstOrDefault((d) => d.Id == eeResult.Item2.DrawingId);
                    var f = new GeneralPlayerDrawingEventViewForm(eeResult.Item2, ed);
                    f.ShowDialog(this);
                    f.Dispose();
                    //Lets initiate the result broadcast
                    imgbtnInitiateResults.PerformClick();
                    SetBtnControlDisable(false);
                }
                             
            }
            else // There was an error.
            {
                if (e.Error is GameTech.Elite.Client.ServerCommException)
                    m_serverCommFailed = true;
                else
                    MessageForm.Show(this, e.Error.Message);
            }

            // Close the wait form.
            m_waitForm.CloseForm();
        }
        
        private void executeEventBtn_Click(object sender, EventArgs e)
        {
            var drawingEvent = drawingEventsLV.SelectedItems[0].Tag as GeneralPlayerDrawingEvent;//get the selected item data

            if(drawingEvent.HeldWhen.HasValue)
            {
                MessageForm.Show("Cannot hold an event that has already been held.", "Execution not permitted", MessageFormTypes.OK);
                return;
            }
            else if (drawingEvent.CancelledWhen.HasValue)
            {
                MessageForm.Show("Cannot hold an event that has been cancelled.", "Execution not permitted", MessageFormTypes.OK);
                return;
            }
            else
            {
                int eventId = drawingEvent.EventId;
               // bool showEvent = false;

                m_waitForm = new WaitForm();
                m_waitForm.WaitImage = Resources.Waiting;
                m_waitForm.CancelButtonVisible = false;
                m_waitForm.ProgressBarVisible = false;
                m_waitForm.Cursor = Cursors.WaitCursor;
                m_waitForm.Message = m_displayText +" event is running please wait. ";

                m_worker = new BackgroundWorker();
                m_worker.WorkerReportsProgress = false;
                m_worker.WorkerSupportsCancellation = false;
                m_worker.DoWork += new DoWorkEventHandler(startexecuteEventDrawing);
                m_worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(executeEventDrawingComplete);                
                m_worker.RunWorkerAsync(eventId);

                // Block until we are finished searching.
                //viewEntriesAndResults();
                m_waitForm.ShowDialog(this);


                // var eeResult = ExecuteGeneralDrawingEventMessage.ExecuteEvent(eventId, true, true);



                //    LoadCurrentAndRecentDrawingEvents(false, false);
                //    if(!eeResult.Item1)
                //    {
                //        String msg = String.Empty;
                //        GeneralPlayerDrawing ed = m_drawings.FirstOrDefault((d) => d.Id == eeResult.Item2.DrawingId);
                //        var minEntryRequired = ed.MinimumEntries;

                //        if(eeResult.Item2.HeldWhen.HasValue)
                //            msg = "Event was already held.";
                //        else if(eeResult.Item2.CancelledWhen.HasValue)
                //            msg = "Cannot hold a cancelled event.";
                //        else if (minEntryRequired > eeResult.Item2.Entries.Count)
                //            msg = "Cannot run the " + m_displayText + "."
                //        +  Environment.NewLine +"The required number of entries has not been met.";
                //        else msg = "Event not executed.";

                //        var dr = MessageForm.Show(msg , "Run " + m_displayText, MessageFormTypes.OK);
                //        showEvent = (dr == System.Windows.Forms.DialogResult.Yes);
                //    }
                //    else
                //        showEvent = true;

                //    if(showEvent)
                //    {
                //        var ed = m_drawings.FirstOrDefault((d) => d.Id == eeResult.Item2.DrawingId);
                //        var f = new GeneralPlayerDrawingEventViewForm(eeResult.Item2, ed);
                //        f.ShowDialog(this);
                //        f.Dispose();
                //        //Lets initiate the result broadcast
                //        imgbtnInitiateResults.PerformClick();
                //        SetBtnControlDisable(false);
                //    }
                //
            }
        }
    
        private void cancelEventBtn_Click(object sender, EventArgs e)
        {
            var drawingEvent = drawingEventsLV.SelectedItems[0].Tag as GeneralPlayerDrawingEvent;
            GeneralPlayerDrawing ed = m_drawings.FirstOrDefault((d) => d.Id == drawingEvent.DrawingId);
            if(drawingEvent.HeldWhen.HasValue)
            {
                MessageForm.Show("Cannot cancel an event that has been held.", "Cancel not permitted", MessageFormTypes.YesNo);
                return;
            }
            else if (drawingEvent.CancelledWhen.HasValue)
            {
                MessageForm.Show("Cannot cancel an event that has already been cancelled.", "Event already cancelled", MessageFormTypes.YesNo);
                return;
            }
            else
            {
                int eventId = drawingEvent.EventId;
                var dr = MessageForm.Show(
                    "Are you sure you want to cancel the " + ed.Name + " " + m_displayText
                    + (drawingEvent.ScheduledForWhen != null ? Environment.NewLine + " scheduled for " + drawingEvent.ScheduledForWhen.Value.ToShortDateString() : "") + 
                    "?", "Cancel Event", MessageFormTypes.YesNo);

                if (dr == System.Windows.Forms.DialogResult.Yes)
                {
                    LoadCurrentAndRecentDrawingEvents(false, false);
                    SetGeneralDrawingEventCancelledMessage.CancelEvent(eventId);
               }              
            }
        }

        private void reinstateEventBtn_Click(object sender, EventArgs e)
        {
            var drawingEvent = drawingEventsLV.SelectedItems[0].Tag as GeneralPlayerDrawingEvent;

            if (!drawingEvent.CancelledWhen.HasValue)
            {
                MessageForm.Show("Cannot reinstate an event that is not cancelled.", "Event not cancelled", MessageFormTypes.YesNo);
                return;
            }
            else
            {
                int eventId = drawingEvent.EventId;
                GeneralPlayerDrawing ed = m_drawings.FirstOrDefault((d) => d.Id == drawingEvent.DrawingId);
                var dr = MessageForm.Show("Are you sure you want to reinstate the " + ed.Name + " " + m_displayText + Environment.NewLine + " scheduled for " + drawingEvent.ScheduledForWhen.Value.ToShortDateString() +"?", "Reinstate Event", MessageFormTypes.YesNo);
               
                if (dr == System.Windows.Forms.DialogResult.Yes)
                {
                    SetGeneralDrawingEventCancelledMessage.ReinstateEvent(eventId);
                    LoadCurrentAndRecentDrawingEvents(false, false);
                }              
            }
        }

        private void viewEntriesAndResultsBtn_Click(object sender, EventArgs e)
        {
            try
            {
                m_waitForm = new WaitForm();
                m_waitForm.WaitImage = Resources.Waiting;
                m_waitForm.CancelButtonVisible = false;
                m_waitForm.ProgressBarVisible = false;
                m_waitForm.Cursor = Cursors.WaitCursor;
                m_waitForm.Message = "Getting result please wait.";

                m_worker = new BackgroundWorker();
                m_worker.WorkerReportsProgress = false;
                m_worker.WorkerSupportsCancellation = false;
                m_worker.DoWork += new DoWorkEventHandler(viewEntriesAndResults);
                m_worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(viewEntriesAndResultsComplete);
                var selEvent = drawingEventsLV.SelectedItems[0].Tag as GeneralPlayerDrawingEvent;//Get the selected Event
                int eventId = selEvent.EventId;//Get the event Id 
                object[] workerArgs = new object[2];
                workerArgs[0] = selEvent;
                workerArgs[1] = eventId;
                m_worker.RunWorkerAsync(workerArgs);

                // Block until we are finished searching.
                //viewEntriesAndResults();
                m_waitForm.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageForm.Show(this, ex.Message);
            }
            finally
            {
                if (m_waitForm != null)
                {
                    m_waitForm.Dispose();
                    m_waitForm = null;
                }
            }
           
        }


        private void viewEntriesAndResults(object sender, DoWorkEventArgs e)
        {

            object[] args = (object[])e.Argument;
            GeneralPlayerDrawingEvent selEvent = (GeneralPlayerDrawingEvent)args[0];
            int eventId = (int)args[1];         
            var drawingEvents = GetGeneralDrawingEventsMessage.GetEvents(selEvent.DrawingId, eventId, DateTime.Now.Date.AddDays(-14), DateTime.Now.Date, true, true);//Run server message but this time using eventId and drawingId and do the calculation.
            object[] resultObject = new object[2];
            resultObject[0] = selEvent;
            resultObject[1] = drawingEvents;
            e.Result = resultObject;       
        }

        private void viewEntriesAndResultsComplete(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error == null)
            {
                object[] result = (object[])e.Result;
                GeneralPlayerDrawingEvent selEvent = (GeneralPlayerDrawingEvent)result[0];
                List<GeneralPlayerDrawingEvent> drawingEvents =  (List<GeneralPlayerDrawingEvent>)result[1];

                var t = drawingEvents.FirstOrDefault();//Get the selected Event - theres only single item here so just use first or default without filter.
                var ed = m_drawings.FirstOrDefault((d) => d.Id == selEvent.DrawingId);//Get the drawing
                var f = new GeneralPlayerDrawingEventViewForm(t, ed);
                f.ShowDialog(this);
                f.Dispose();
            }
            else // There was an error.
            {
                if (e.Error is GameTech.Elite.Client.ServerCommException)
                    m_serverCommFailed = true;
                else
                    MessageForm.Show(this, e.Error.Message);
            }

            // Close the wait form.
            m_waitForm.CloseForm();
        }

        private void initiateEventResultsBroadcastBtn_Click(object sender, EventArgs e)
        {
            var drawingEvent = drawingEventsLV.SelectedItems[0].Tag as GeneralPlayerDrawingEvent;

            if(!drawingEvent.HeldWhen.HasValue)
            {
                MessageForm.Show("Cannot initiate broadcast for an event that has not been held.", "Event not held", MessageFormTypes.OK);
                return;
            }
            else
            {
                int eventId = drawingEvent.EventId;
                var displayInitiated = InitiateGeneralDrawingEventResultsNotificationsMessage.InitiateResultsNotifications(eventId);

                string msg = null;
                if(displayInitiated)
                    msg = String.Format("Event {0} broadcast initiated.", eventId);
                else
                    msg = String.Format("Event {0} broadcast not initiated.", eventId);

                MessageForm.Show(msg, "Initiate Broadcast results", MessageFormTypes.OK);
            }
        }

        private void abortEventResultsBroadcastBtn_Click(object sender, EventArgs e)
        {
            AbortGeneralDrawingEventResultsNotificationsMessage.AbortResultsNotifications();
        }

        private void imgBtnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }
   
        private void m_cmbxAvailableRaffles_SelectedIndexChanged(object sender, EventArgs e)
        {
            drawingEventsLV.Items.Clear();
            drawingEventsLV.Columns.Clear();
            //drawingEventsLV.BeginUpdate();
            drawingEventsLV.HideSelection = true;
            LoadCurrentAndRecentDrawingEvents(false, false);
            SetBtnControlDisable(false);
        }
    }
}
