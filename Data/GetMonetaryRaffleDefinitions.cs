﻿#region Copyright
// This is an unpublished work protected under the copyright laws of the United
// States and other countries.  All rights reserved.  Should publication occur
// the following will apply:  © 2008-2017 GameTech International, Inc.
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GTI.Modules.Shared;
using System.IO;

namespace GTI.Modules.PlayerCenter.Data
{
    /// <summary>
    /// Represents a message to return one or many monetary raffle definitions
    /// </summary>
    public class GetMonetaryRaffleDefinitions : ServerMessage
    {
        #region Private Members

        private int? m_raffleID;

        #endregion

        #region Public Properties

        public int? RaffleID
        {
            get { return m_raffleID; }
            set
            {
                m_raffleID = value;
            }
        }

        public List<MonetaryRaffle> Raffles
        {
            get;
            set;
        }

        #endregion

        public GetMonetaryRaffleDefinitions(int? id)
        {
            m_id = 18240;
            m_strMessageName = "Get Monetary Raffle Definitions";

            m_raffleID = id;
            Raffles = new List<MonetaryRaffle>();
        }

        #region Member Methods

        /// <summary>
        /// Either returns the raffle that matches the sent in ID or all monetary raffles
        /// </summary>
        /// <param name="gamingDate"></param>
        /// <returns></returns>
        public static List<MonetaryRaffle> GetMonetaryRaffles(int? id = null)
        {
            var msg = new GetMonetaryRaffleDefinitions(id);
            try
            {
                msg.Send();
            }
            catch (ServerCommException ex)
            {
                throw new Exception(msg.MessageName + " Message: " + ex.Message);
            }

            return msg.Raffles;
        }

        /// <summary>
        /// Prepares the request to be sent to the server.
        /// </summary>
        protected override void PackRequest()
        {
            // Create the streams we will be writing to.
            using (var requestStream = new MemoryStream())
            using (var requestWriter = new BinaryWriter(requestStream, Encoding.Unicode))
            {
                //Drawing ID
                requestWriter.Write(m_raffleID ?? 0);

                // Set the bytes to be sent.
                m_requestPayload = requestStream.ToArray();

                // Close the streams.
                requestWriter.Close();
            }
        }

        /// <summary>
        /// Parses the response received from the server.
        /// </summary>
        protected override void UnpackResponse()
        {
            base.UnpackResponse();

            // Create the streams we will be reading from.
            using (var responseStream = new MemoryStream(m_responsePayload))
            using (var reader = new BinaryReader(responseStream, Encoding.Unicode))
            {
                // Try to unpack the data.
                try
                {
                    // Seek past return code.
                    reader.BaseStream.Seek(sizeof(int), SeekOrigin.Begin);

                    Raffles = new List<MonetaryRaffle>();

                    // Raffle Count
                    ushort raffleCount = reader.ReadUInt16();

                    // Get all the menus.
                    for (ushort x = 0; x < raffleCount; x++)
                    {
                        MonetaryRaffle raffle = new MonetaryRaffle();

                        raffle.ID = reader.ReadInt32();
                        raffle.Name = ReadString(reader);
                        raffle.Description = ReadString(reader);

                        ushort optionCount = reader.ReadUInt16();

                        for (ushort y = 0; y < optionCount; y++)
                        {
                            MonetaryRafflePrizes option = new MonetaryRafflePrizes();

                            option.Value = ReadDecimal(reader) ?? 0.00m;
                            option.Weight = reader.ReadInt32();

                            raffle.Options.Add(option);
                        }

                        Raffles.Add(raffle);
                    }
                }
                catch (EndOfStreamException e)
                {
                    throw new MessageWrongSizeException(m_strMessageName, e);
                }
                catch (Exception e)
                {
                    throw new ServerException(m_strMessageName, e);
                }
            }
        }
        #endregion
    }
}
