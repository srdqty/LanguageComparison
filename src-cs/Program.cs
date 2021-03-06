using System;
using System.Collections.Generic;
using System.Transactions;
using System.IO;

namespace CSharpPerfEval
{
    class Program
    {
        static void Main(string[] args)
        {
            var testExchangeData = new List<ExchangeRate>()
            {
                new ExchangeRate(Currency.USD, Currency.MXN, 1.5),
                new ExchangeRate(Currency.USD, Currency.EUD, 2.5),
                new ExchangeRate(Currency.USD, Currency.THB, 3.5),
                new ExchangeRate(Currency.USD, Currency.GBP, 4.5),


                new ExchangeRate(Currency.MXN, Currency.EUD, 2.5),
                new ExchangeRate(Currency.MXN, Currency.THB, 3.5),
                new ExchangeRate(Currency.MXN, Currency.GBP, 4.5),

                new ExchangeRate(Currency.EUD, Currency.THB, 3.5),
                new ExchangeRate(Currency.EUD, Currency.GBP, 4.5),

                new ExchangeRate(Currency.THB, Currency.GBP, 4.5)

            };

            var converter = new CurrencyConverter(testExchangeData);

            var reader = new FileReader();

            var accounts = new AccountParser().ParseFile(reader.ReadFile("../accounts1.2m.txt"));
            var transactions = new TransactionParser().ParseFile(reader.ReadFile("../transactions10m.txt"));

            var processor = new Processor(converter, accounts, transactions);

            processor.Process();

            using (var file = new StreamWriter("output.txt"))
            {
                foreach (var account in accounts)
                {
                    file.WriteLine(account.Value);
                }
            }

            Console.WriteLine("Done");
        }
    }

    //Import file into Data Structures

    public enum Currency
    {
        USD,
        MXN,
        EUD,
        THB,
        GBP
    }

    public class Account
    {
        public string AccountNumber { get; set; }
        public string Name { get; set; }
        public double BalanceAmount { get; set; }
        public Currency BalanceCurrency { get; set; }

        public override string ToString()
        {
            return $"AccountNumber: {AccountNumber}, Name: {Name}, Balance: {BalanceAmount}, Currency: {BalanceCurrency.ToString()}";
        }
    }

    public abstract class Transaction
    {
        public string AccountNumber { get; set; }
        public double Amount { get; set; }
        public Currency Currency { get; set; }

    }

    public class Bill : Transaction
    {
        public string Bucket { get; set; }
    }

    public class Payment : Transaction
    {
        public string Source { get; set; }
    }


    public class CurrencyConverter
    {
        public readonly Dictionary<Currency, Dictionary<Currency, ExchangeRate>> ExchangeRates;

        public CurrencyConverter(List<ExchangeRate> rates)
        {
            // from              // to      // rate
            ExchangeRates = new Dictionary<Currency, Dictionary<Currency, ExchangeRate>>();

            var dict = new Dictionary<Currency, ExchangeRate>();

            foreach (var rate in rates)
            {
                if (!ExchangeRates.ContainsKey(rate.From))
                {
                    ExchangeRates.Add(rate.From, new Dictionary<Currency, ExchangeRate>());
                }

                if (!ExchangeRates.ContainsKey(rate.To))
                {
                    ExchangeRates.Add(rate.To, new Dictionary<Currency, ExchangeRate>());
                }
            }

            foreach (var rate in rates)
            {
                ExchangeRates[rate.From][rate.To] = rate;
                ExchangeRates[rate.To][rate.From] = new ExchangeRate(rate.From, rate.To, 1 / rate.Rate);
            }
        }

        public double Convert(Currency from, Currency to, double amount)
        {
            return ExchangeRates[from][to].Rate * amount;
        }
    }

    public class ExchangeRate
    {
        public ExchangeRate(Currency from, Currency to, double rate)
        {
            From = from;
            To = to;
            Rate = rate;
        }

        public Currency From { get; }
        public Currency To { get; }
        public double Rate { get; }
    }

    public class Processor
    {
        CurrencyConverter _converter;
        Dictionary<String, Account> accounts;
        List<Transaction> transactions;


        public Processor(CurrencyConverter converter, Dictionary<String, Account> accounts, List<Transaction> transactions)
        {
            _converter = converter;
            this.accounts = accounts;
            this.transactions = transactions;
        }

        public void Process()
        {
            var transactionsForAccount = new Dictionary<string, List<Transaction>>();

            foreach (var transaction in transactions)
            {
                if (!transactionsForAccount.ContainsKey(transaction.AccountNumber))
                {
                    transactionsForAccount[transaction.AccountNumber] = new List<Transaction>();
                }

                transactionsForAccount[transaction.AccountNumber].Add(transaction);
            }

            foreach (var kvp in transactionsForAccount)
            {
                Account account;
                if (accounts.TryGetValue(kvp.Key, out account))
                {
                    foreach (var trans in kvp.Value)
                    {
                        ApplyTransactionToAccount(account, trans);
                    }
                }
            }
        }


        private void ApplyTransactionToAccount(Account acct, Transaction trans)
        {
            var amount = trans.Amount;
            if (trans.Currency != acct.BalanceCurrency)
            {
                amount = _converter.Convert(trans.Currency, acct.BalanceCurrency, trans.Amount);
            }

            switch (trans)
            {
                case Bill b:
                    acct.BalanceAmount += amount;
                    break;
                case Payment p:
                    acct.BalanceAmount -= amount;
                    break;
                default:
                    throw new Exception();
            }
        }
    }
    public class TransactionParser
    {
        public List<Transaction> ParseFile(string content)
        {
            var transactions = new List<Transaction>();
            foreach (var line in content.Split("\n"))
            {
                var columns = line.Split("|");
                Transaction transaction = null;
                if (TryParse(columns, ref transaction))
                {
                    transactions.Add(transaction);
                }
                else
                {
                    Console.WriteLine($"Failed to import transaction: {line}");
                }
            }

            return transactions;
        }

        public bool TryParse(string[] columns, ref Transaction transaction)
        {
            if (columns.Length != 4)
                return false;

            int account;
            if (!int.TryParse(columns[0], out account))
            {
                return false;
            }

            var moneyParts = columns[1].Split(" ");
            double currencyAmount;
            Currency currencyType;
            if (moneyParts.Length != 2
                || !double.TryParse(moneyParts[0], out currencyAmount)
                || !Enum.TryParse(moneyParts[1], out currencyType))
            {
                return false;
            }


            if (columns[2] == "Bill")
            {
                transaction = new Bill()
                {
                    AccountNumber = account.ToString(),
                    Amount = currencyAmount,
                    Currency = currencyType,
                    Bucket = columns[3]
                };
                return true;
            }
            if (columns[2] == "Payment")
            {
                transaction = new Payment()
                {
                    AccountNumber = account.ToString(),
                    Amount = currencyAmount,
                    Currency = currencyType,
                    Source = columns[3]
                };
                return true;
            }

            return false;
        }
    }


    public class AccountParser
    {
        public Dictionary<String, Account> ParseFile(string content)
        {
            var accounts = new Dictionary<String, Account>();
            foreach (var line in content.Split("\n"))
            {
                var columns = line.Split("|");
                Account account = null;
                if (TryParse(columns, ref account))
                {
                    accounts.Add(account.AccountNumber, account);
                }
                else
                {
                    Console.WriteLine($"Failed to import account: {line}");
                }
            }

            return accounts;
        }
        public bool TryParse(string[] columns, ref Account account)
        {
            if (columns.Length != 3)
                return false;

            int result;
            if (!int.TryParse(columns[0], out result))
            {
                return false;
            }

            var moneyParts = columns[1].Split(" ");
            double currencyAmount;
            Currency currencyType;
            if (moneyParts.Length != 2
                || !double.TryParse(moneyParts[0], out currencyAmount)
                || !Enum.TryParse(moneyParts[1], out currencyType))
            {
                return false;
            }

            account = new Account()
            {
                AccountNumber = result.ToString(),
                BalanceAmount = currencyAmount,
                BalanceCurrency = currencyType,
                Name = columns[2]
            };

            return true;
        }
    }

    public class FileReader
    {
        public string ReadFile(string path)
        {
            return System.IO.File.ReadAllText(path);
        }
    }
}