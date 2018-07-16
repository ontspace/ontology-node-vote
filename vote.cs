using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.Numerics;
using Helper = Neo.SmartContract.Framework.Helper;

namespace StockAssign
{
    public class Contract1 : SmartContract
    {
        public static readonly byte[] ontAddr = "AFmseVrdL9f9oyCzZefL9tG6UbvhUMqNMV".ToScriptHash();
        public static readonly byte[] ongAddr = "AFmseVrdL9f9oyCzZefL9tG6UbvhfRZMHJ".ToScriptHash();
        public static readonly byte[] govAddr = "AFmseVrdL9f9oyCzZefL9tG6UbviEH9ugK".ToScriptHash();

        public static readonly byte[] admin = "AMAx993nE6NEqZjwBssUfopxnnvTdob9ij".ToScriptHash();
        public static readonly byte[] recycleAddr = "AH2Y9ngDT7m6KAQfNpr7H4BqVCdJJSttmV".ToScriptHash();
        public static readonly string pubKey = "02890c587f4e4a6a98b455248eabac04b733580cfe5f11acd648c675543dfbb926";

        private const ulong factor = 1000000000; // ONG Decimal
        private const ulong totalCap = 1000000; // 100W
        private const ulong ongRate = 5; // 一轮周期后每一百个ONT分润的ONG数量

        private const ulong beginBlock = 8650;
        private const ulong endBlock = 8750;
        private const ulong quitBlock = 8800;


        public static Object Main(string operation, params object[] args)
        {
            if (operation == "Vote")
            {
                if (args.Length != 2) return false;
                byte[] from = (byte[])args[0];
                ulong value = (ulong)args[1];
                return Vote(from, value);
            }

            if (operation == "VoteToPeer")
            {
                if (args.Length != 1) return false;
                string pubKey = (string)args[0];
                return VoteToPeer(pubKey);
            }

            if (operation == "Unvote")
            {
                if (args.Length != 2) return false;
                byte[] from = (byte[])args[0];
                ulong value = (ulong)args[1];
                return Unvote(from, value);
            }

            if (operation == "QuitNode")
            {
                return QuitNode();
            }

            return false;
        }

        public static bool Vote(byte[] from, ulong value)
        {
            if (value <= 0)
            {
                return false;
            }
            if (!Runtime.CheckWitness(from))
            {
                Runtime.Notify("Checkwitness failed.");
                return false;
            }
            uint height = Blockchain.GetHeight();
            if (height >= beginBlock)
            {
                Runtime.Notify("current block greater than beginblock, can't vote.");
                return false;
            }
            
            byte[] voteContract = ExecutionEngine.ExecutingScriptHash;

            StorageContext context = Storage.CurrentContext;
            ulong totalAmount = (ulong)Storage.Get(context, "totalAmount").AsBigInteger();
            if (totalAmount < 0)
            {
                return false;
            }
            string flag = Storage.Get(context, "votepeerflag").AsString();
            if (flag == "true")
            {
                Runtime.Notify("vote to peer has been finished.");
                return false;
            }
            ulong balanceCap = totalCap - totalAmount;
            if (value > balanceCap)
            {
                value = balanceCap;
            }

            Transfer param = new Transfer { From = from, To = voteContract, Value = value };
            object[] p = new object[1];
            p[0] = param;
            byte[] ret = Native.Invoke(0, ontAddr, "transfer", p);
            if (ret[0] != 1)
            {
                return false;
            }
            BigInteger balance = Storage.Get(context, from).AsBigInteger();
            Storage.Put(context, from, balance + value);
            Storage.Put(context, "totalAmount", totalAmount + value);
            return true;
        }

        public static bool Unvote(byte[] from, ulong value)
        {
            byte[] voteContract = ExecutionEngine.ExecutingScriptHash;
            byte[] ret;
            //if (!Runtime.CheckWitness(admin))
            //{
            //    return false;
            //}
            uint height = Blockchain.GetHeight();
            if (height < beginBlock)
            {
                Runtime.Notify("can't unvote before beign block height");
                return false;
            }
            StorageContext context = Storage.CurrentContext;
            ulong voteValue = (ulong)Storage.Get(context, from).AsBigInteger();
            if (voteValue < value)
            {
                Runtime.Notify("withdraw value more than voted value");
                return false;
            }
            
            ulong totalAmount = (ulong)Storage.Get(context, "totalAmount").AsBigInteger();
            if (totalAmount < 0)
            {
                return false;
            }
                
            string flag = Storage.Get(context, "unvotepeerflag").AsString();
            Runtime.Notify(flag);
            if (flag == "false")
            {
                Peer peer = new Peer { From = voteContract, Key = new string[] { pubKey }, Value = new ulong[] { totalAmount } };

                ret = Native.Invoke(0, govAddr, "unVoteForPeer", peer);
                if (ret[0] != 1)
                {
                    Runtime.Notify("unvote peer failed");
                    return false;
                }
                Storage.Put(context, "unvotepeerflag", "true");
                Runtime.Notify("unvote success, need to waiting ending block height.");
                return true;
            }

            if (height < endBlock)
            {
                Runtime.Notify("need to wait ending block to withdraw.");
                return false;
            }

            flag = Storage.Get(context, "withdrawflag").AsString();
            Runtime.Notify(flag);

            // withdraw ONT from vote peer node
            if (flag == "false")
            {
                Withdraw w = new Withdraw { Account = voteContract, PeerPubkey = new string[] { pubKey }, Value = new ulong[] { totalAmount } };
                ret = Native.Invoke(0, govAddr, "withdraw", w);
                if (ret[0] != 1)
                {
                    Runtime.Notify("withdraw ont failed");
                    return false;
                }
                Runtime.Notify("withdraw ont success");
                Storage.Put(context, "withdrawflag", "true");
            }
            

            // reback ONT to voter
            object[] p = new object[1];
            Transfer t = new Transfer { From = voteContract, To = from, Value = value };
            p[0] = t;
            ret = Native.Invoke(0, ontAddr, "transfer", p);
            if (ret[0] != 1)
            {
                return false;
            }

            // reback ONG to voter
            ulong ongValue = (value * ongRate * factor) / 100 ;
            t.Value = ongValue;
            p[0] = t;
            ret = Native.Invoke(0, ongAddr, "transfer", p);
            if (ret[0] != 1)
            {
                return false;
            }

            Storage.Put(context, from, voteValue - value);
            return true;
        }

        public static bool VoteToPeer(string pubKey)
        {
            if (!Runtime.CheckWitness(admin))
            {
                return false;
            }

            byte[] voteContract = ExecutionEngine.ExecutingScriptHash;

            uint height = Blockchain.GetHeight();
            if (height >= beginBlock)
            {
                Runtime.Notify("current blockheight greater than beign block height");
                return false;
            }

            StorageContext context = Storage.CurrentContext;

            ulong totalOnt = (ulong)Storage.Get(context, "totalAmount").AsBigInteger();
            if (totalOnt < 0)
            {
                return false;
            }

            Approve approve = new Approve { From = voteContract, To = govAddr, Value = (ulong)totalOnt };

            byte[] ret = Native.Invoke(0, ontAddr, "approve", approve);
            if (ret[0] != 1)
            {
                Runtime.Notify("VoteToPeer approve ont failed.");
                return false;
            }

            Peer peer = new Peer { From = voteContract, Key = new string[] { pubKey }, Value = new ulong[] { (ulong)totalOnt } };

            ret = Native.Invoke(0, govAddr, "voteForPeerTransferFrom", peer);
            if (ret[0] != 1)
            {
                Runtime.Notify("votepeer failed");
                return false;
            }
            Runtime.Notify("votepeer success");
            Storage.Put(context, "votepeerflag", "true");
            Storage.Put(context, "unvotepeerflag", "false");
            Storage.Put(context, "withdrawflag", "false");
            return true;
        }

        public static bool QuitNode()
        {
            byte[] voteContract = ExecutionEngine.ExecutingScriptHash;
            if (!Runtime.CheckWitness(admin))
            {
                return false;
            }

            uint height = Blockchain.GetHeight();
            if (height < quitBlock)
            {
                Runtime.Notify("current blockheight less than quit block height");
                return false;
            }

            StorageContext context = Storage.CurrentContext;
            Balance param = new Balance { Account = voteContract };
            ulong remainOnt = (ulong)Native.Invoke(0, ontAddr, "balanceOf", param).AsBigInteger();
            ulong remainOng = (ulong)Native.Invoke(0, ongAddr, "balanceOf", param).AsBigInteger();

            return RecycleAsset(voteContract, recycleAddr, remainOnt, remainOng);
        }

        private static bool RecycleAsset(byte[] from, byte[] to, ulong ont, ulong ong)
        {
            byte[] ret;
            Transfer transfer = new Transfer { From = from, To = to, Value = ont };
            object[] p = new object[1];
            p[0] = transfer;
            
            ret = Native.Invoke(0, ontAddr, "transfer", p);
            if (ret[0] != 1)
            {
                return false;
            }
            transfer.Value = ong;
            p[0] = transfer;
            ret = Native.Invoke(0, ongAddr, "transfer", p);
            if (ret[0] != 1)
            {
                return false;
            }
            return true;
        }

        struct Transfer
        {
            public byte[] From;
            public byte[] To;
            public ulong Value;
        }

        struct Withdraw
        {
            public byte[] Account;
            public string[] PeerPubkey;
            public ulong[] Value;
        }

        struct Peer
        {
            public byte[] From;
            public string[] Key;
            public ulong[] Value;
        }

        struct Balance
        {
            public byte[] Account;
        }

        struct Approve
        {
            public byte[] From;
            public byte[] To;
            public ulong Value;
        }
    }
}
