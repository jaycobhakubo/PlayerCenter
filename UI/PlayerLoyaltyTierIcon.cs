﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GTI.Modules.Shared;
using System.Drawing.Drawing2D;
using System.Globalization;
using GTI.Modules.PlayerCenter.Data;
using System.IO;
using GTI.Modules.Shared.Business;

namespace GTI.Modules.PlayerCenter.UI
{
    public partial class PlayerLoyaltyTierIcon : GradientForm//EliteGradientForm
    {
        #region Member Variables
        int m_widthIconDistance = 10;
        int m_heightIconDistance = 2;
        List<string> files = new List<string>();
        int maxHeight = -1;
        //private List<byte[]> m_lstbyteIconList = new List<byte[]>();
        private List<TierIcon> m_lstTierIcon;

        #endregion

        #region CONSTRUCTOR
        public PlayerLoyaltyTierIcon(List<TierIcon> tierIcon_)
        {
            InitializeComponent();
            DrawGradient = true;
            m_lstTierIcon = tierIcon_;
            PopulateTierIcon();       
        }


        
        private void UpdateUIIconImageLocation()
        {
            if (m_lstTierIcon.Count > 20 /*m_lstbyteIconList.Count() > 20*/)
            {
                Size _size = new Size(383, 301);
                groupBox1.Size = _size;
                groupBox1.Location = new Point(5, 3);
                _size = new Size(377, 282);
                m_pnlIconTier.Size = _size;
            }
            else
            {
                Size _size = new Size(366, 301);
                groupBox1.Size = _size;
                groupBox1.Location = new Point(15, 3);
                _size = new Size(360, 282);
                m_pnlIconTier.Size = _size;
            }
        }


        #endregion
        #region POPULATE ICON     
        
        private void PopulateTierIcon()
        {
            foreach (TierIcon data_ in m_lstTierIcon)
            {
                PictureBox pb = new PictureBox();
                pb.Tag = data_.TierIconId;
                Size _size = new Size(60, 60);
                pb.Size = _size;
                pb.Click += new EventHandler(pictureBox1_Click);
                pb.Location = new Point(m_widthIconDistance, m_heightIconDistance);
                pb.SizeMode = PictureBoxSizeMode.StretchImage;
                pb.Image = data_.TierIconImage;
                maxHeight = Math.Max(pb.Height, maxHeight);
                m_widthIconDistance += pb.Width + 10;
                if (m_widthIconDistance > this.ClientSize.Width - 60)
                {
                    m_widthIconDistance = 10;
                    m_heightIconDistance += maxHeight + 10;
                }

                m_pnlIconTier.Controls.Add(pb);               
            }
            UpdateUIIconImageLocation();           
        }    
        #endregion  

        #region IMPORT IMAGE
        private void m_imgbtnImport_Click(object sender, EventArgs e)
        {
            OpenFileDialog  openFileDialog1= new OpenFileDialog();
            openFileDialog1.Multiselect = true;
            openFileDialog1.Filter = "JPG|*.jpg|JPEG|*.jpeg|GIF|*.gif|PNG|*.png";
         
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                string[] _selectedImgIcon = openFileDialog1.FileNames;

                foreach (string _imgIcon in _selectedImgIcon)
                {
                    var pic = new PictureBox();
                    byte[] imageData;
                                   
                    Size _size = new Size(60, 60);
                    pic.Size = _size;
                    pic.Location = new Point(m_widthIconDistance, m_heightIconDistance);
                    pic.SizeMode = PictureBoxSizeMode.StretchImage;
                    pic.Image = Image.FromFile(_imgIcon);
                    pic.Click += new EventHandler(pictureBox1_Click);

                    MemoryStream mStream = new MemoryStream();
                    pic.Image.Save(mStream, System.Drawing.Imaging.ImageFormat.Jpeg);
                    imageData = mStream.ToArray();   

                    m_widthIconDistance += pic.Width + 10;
                    maxHeight = Math.Max(pic.Height, maxHeight);

                    if (m_widthIconDistance > this.ClientSize.Width - 60)
                    {
                        m_widthIconDistance = 10;
                        m_heightIconDistance += maxHeight + 10;
                    }
                    var tTierIcon = SetPlayerTierIcon.Msg(imageData);
                    pic.Tag = tTierIcon.TierIconId;
                    m_lstTierIcon.Add(tTierIcon);      
                    m_pnlIconTier.Controls.Add(pic);
         
                }
               UpdateUIIconImageLocation();            
            }
        }

        private TierIcon m_tierIcon;

        #endregion   

        PictureBox m_pctbxSelected = new PictureBox();
        //PictureBox m_pctbxPreviousSelectd e= new PictureBox();

        private void pictureBox1_Click(object sender, EventArgs e)
        {
            m_pctbxSelected.BorderStyle = BorderStyle.None;
            m_pctbxSelected.Width -= 2;
            m_pctbxSelected.Height -= 2;

            PictureBox pctbx = (PictureBox)sender;
            pctbx.BorderStyle = BorderStyle.FixedSingle;
            pctbx.Width += 2;
            pctbx.Height +=  2;
            m_pctbxSelected = pctbx;

            m_tierIcon = m_lstTierIcon.Single(l => l.TierIconId == (int)pctbx.Tag);
           
        }

        private void imgbtnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void m_imgbtnSelect_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            this.Close();
        }


        public TierIcon SelectedTierIcon 
        { 
            get {return m_tierIcon;}  
            //set; 
        }

        //public int TierIconId
        //{
        //    get 
        //    {
        //        return m_tierIconId;// return (int)m_pctbxSelected.Tag; 
        //    }
        //}



        public Image SelectedImage
        {
            get 
            { 
                return m_pctbxSelected.Image; 
            }
        }

        private void m_imgbtnDelete_Click(object sender, EventArgs e)
        {
            m_pnlIconTier.Controls.Remove(m_pctbxSelected);       
        }
    }
}


//#region HELPER
//private Color SetColor( string _hexColor)
//{
//    Color _color;
//    int argb = Int32.Parse(_hexColor.Replace("#", ""), NumberStyles.HexNumber);
//    _color = Color.FromArgb(argb);
//    return _color;
//}
//#endregion
