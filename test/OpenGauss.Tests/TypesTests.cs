using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using OpenGauss.NET.Util;
using OpenGauss.NET.Types;
using NUnit.Framework;
using OpenGauss.NET;

#pragma warning disable 618 // OpenGaussDateTime, OpenGaussDate, OpenGaussTimeSpan are obsolete, remove in 7.0

namespace OpenGauss.Tests
{
    /// <summary>
    /// Tests OpenGauss.NET.Types.* independent of a database
    /// </summary>
    [TestFixture]
    public class TypesTests
    {
        [Test]
        public void OpenGaussIntervalParse()
        {
            string input;
            OpenGaussTimeSpan test;

            input = "1 day";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(TimeSpan.FromDays(1).Ticks), input);

            input = "2 days";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(TimeSpan.FromDays(2).Ticks), input);

            input = "2 days 3:04:05";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(new TimeSpan(2, 3, 4, 5).Ticks), input);

            input = "-2 days";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(TimeSpan.FromDays(-2).Ticks), input);

            input = "-2 days -3:04:05";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(new TimeSpan(-2, -3, -4, -5).Ticks), input);

            input = "-2 days -0:01:02";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(new TimeSpan(-2, 0, -1, -2).Ticks), input);

            input = "2 days -12:00";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(new TimeSpan(2, -12, 0, 0).Ticks), input);

            input = "1 mon";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(TimeSpan.FromDays(30).Ticks), input);

            input = "2 mons";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(TimeSpan.FromDays(60).Ticks), input);

            input = "1 mon -1 day";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(TimeSpan.FromDays(29).Ticks), input);

            input = "1 mon -2 days";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(TimeSpan.FromDays(28).Ticks), input);

            input = "-1 mon -2 days -3:04:05";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(new TimeSpan(-32, -3, -4, -5).Ticks), input);

            input = "1 year";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(TimeSpan.FromDays(30 * 12).Ticks), input);

            input = "2 years";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(TimeSpan.FromDays(30 * 24).Ticks), input);

            input = "1 year -1 mon";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(TimeSpan.FromDays(30 * 11).Ticks), input);

            input = "1 year -2 mons";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(TimeSpan.FromDays(30 * 10).Ticks), input);

            input = "1 year -1 day";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(TimeSpan.FromDays(30 * 12 - 1).Ticks), input);

            input = "1 year -2 days";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(TimeSpan.FromDays(30 * 12 - 2).Ticks), input);

            input = "1 year -1 mon -1 day";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(TimeSpan.FromDays(30 * 11 - 1).Ticks), input);

            input = "1 year -2 mons -2 days";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(TimeSpan.FromDays(30 * 10 - 2).Ticks), input);

            input = "1 day 2:3:4.005";
            test = OpenGaussTimeSpan.Parse(input);
            Assert.That(test.TotalTicks, Is.EqualTo(new TimeSpan(1, 2, 3, 4, 5).Ticks), input);

            var testCulture = new CultureInfo("fr-FR");
            Assert.That(testCulture.NumberFormat.NumberDecimalSeparator, Is.EqualTo(","), "decimal seperator");
            using (TestUtil.SetCurrentCulture(testCulture))
            {
                input = "1 day 2:3:4.005";
                test = OpenGaussTimeSpan.Parse(input);
                Assert.That(test.TotalTicks, Is.EqualTo(new TimeSpan(1, 2, 3, 4, 5).Ticks), input);
            }
        }

        [Test]
        public void OpenGaussIntervalConstructors()
        {
            OpenGaussTimeSpan test;

            test = new OpenGaussTimeSpan();
            Assert.That(test.Months, Is.EqualTo(0), "Months");
            Assert.That(test.Days, Is.EqualTo(0), "Days");
            Assert.That(test.Hours, Is.EqualTo(0), "Hours");
            Assert.That(test.Minutes, Is.EqualTo(0), "Minutes");
            Assert.That(test.Seconds, Is.EqualTo(0), "Seconds");
            Assert.That(test.Milliseconds, Is.EqualTo(0), "Milliseconds");
            Assert.That(test.Microseconds, Is.EqualTo(0), "Microseconds");

            test = new OpenGaussTimeSpan(1234567890);
            Assert.That(test.Months, Is.EqualTo(0), "Months");
            Assert.That(test.Days, Is.EqualTo(0), "Days");
            Assert.That(test.Hours, Is.EqualTo(0), "Hours");
            Assert.That(test.Minutes, Is.EqualTo(2), "Minutes");
            Assert.That(test.Seconds, Is.EqualTo(3), "Seconds");
            Assert.That(test.Milliseconds, Is.EqualTo(456), "Milliseconds");
            Assert.That(test.Microseconds, Is.EqualTo(456789), "Microseconds");

            test = new OpenGaussTimeSpan(new TimeSpan(1, 2, 3, 4, 5)).JustifyInterval();
            Assert.That(test.Months, Is.EqualTo(0), "Months");
            Assert.That(test.Days, Is.EqualTo(1), "Days");
            Assert.That(test.Hours, Is.EqualTo(2), "Hours");
            Assert.That(test.Minutes, Is.EqualTo(3), "Minutes");
            Assert.That(test.Seconds, Is.EqualTo(4), "Seconds");
            Assert.That(test.Milliseconds, Is.EqualTo(5), "Milliseconds");
            Assert.That(test.Microseconds, Is.EqualTo(5000), "Microseconds");

            test = new OpenGaussTimeSpan(3, 2, 1234567890);
            Assert.That(test.Months, Is.EqualTo(3), "Months");
            Assert.That(test.Days, Is.EqualTo(2), "Days");
            Assert.That(test.Hours, Is.EqualTo(0), "Hours");
            Assert.That(test.Minutes, Is.EqualTo(2), "Minutes");
            Assert.That(test.Seconds, Is.EqualTo(3), "Seconds");
            Assert.That(test.Milliseconds, Is.EqualTo(456), "Milliseconds");
            Assert.That(test.Microseconds, Is.EqualTo(456789), "Microseconds");

            test = new OpenGaussTimeSpan(1, 2, 3, 4);
            Assert.That(test.Months, Is.EqualTo(0), "Months");
            Assert.That(test.Days, Is.EqualTo(1), "Days");
            Assert.That(test.Hours, Is.EqualTo(2), "Hours");
            Assert.That(test.Minutes, Is.EqualTo(3), "Minutes");
            Assert.That(test.Seconds, Is.EqualTo(4), "Seconds");
            Assert.That(test.Milliseconds, Is.EqualTo(0), "Milliseconds");
            Assert.That(test.Microseconds, Is.EqualTo(0), "Microseconds");

            test = new OpenGaussTimeSpan(1, 2, 3, 4, 5);
            Assert.That(test.Months, Is.EqualTo(0), "Months");
            Assert.That(test.Days, Is.EqualTo(1), "Days");
            Assert.That(test.Hours, Is.EqualTo(2), "Hours");
            Assert.That(test.Minutes, Is.EqualTo(3), "Minutes");
            Assert.That(test.Seconds, Is.EqualTo(4), "Seconds");
            Assert.That(test.Milliseconds, Is.EqualTo(5), "Milliseconds");
            Assert.That(test.Microseconds, Is.EqualTo(5000), "Microseconds");

            test = new OpenGaussTimeSpan(1, 2, 3, 4, 5, 6);
            Assert.That(test.Months, Is.EqualTo(1), "Months");
            Assert.That(test.Days, Is.EqualTo(2), "Days");
            Assert.That(test.Hours, Is.EqualTo(3), "Hours");
            Assert.That(test.Minutes, Is.EqualTo(4), "Minutes");
            Assert.That(test.Seconds, Is.EqualTo(5), "Seconds");
            Assert.That(test.Milliseconds, Is.EqualTo(6), "Milliseconds");
            Assert.That(test.Microseconds, Is.EqualTo(6000), "Microseconds");

            test = new OpenGaussTimeSpan(1, 2, 3, 4, 5, 6, 7);
            Assert.That(test.Months, Is.EqualTo(14), "Months");
            Assert.That(test.Days, Is.EqualTo(3), "Days");
            Assert.That(test.Hours, Is.EqualTo(4), "Hours");
            Assert.That(test.Minutes, Is.EqualTo(5), "Minutes");
            Assert.That(test.Seconds, Is.EqualTo(6), "Seconds");
            Assert.That(test.Milliseconds, Is.EqualTo(7), "Milliseconds");
            Assert.That(test.Microseconds, Is.EqualTo(7000), "Microseconds");
        }

        [Test]
        public void OpenGaussIntervalToString()
        {
            Assert.That(new OpenGaussTimeSpan().ToString(), Is.EqualTo("00:00:00"));

            Assert.That(new OpenGaussTimeSpan(1234567890).ToString(), Is.EqualTo("00:02:03.456789"));

            Assert.That(new OpenGaussTimeSpan(1234567891).ToString(), Is.EqualTo("00:02:03.456789"));

            Assert.That(new OpenGaussTimeSpan(new TimeSpan(1, 2, 3, 4, 5)).JustifyInterval().ToString(), Is.EqualTo("1 day 02:03:04.005"));

            Assert.That(new OpenGaussTimeSpan(3, 2, 1234567890).ToString(), Is.EqualTo("3 mons 2 days 00:02:03.456789"));

            Assert.That(new OpenGaussTimeSpan(1, 2, 3, 4).ToString(), Is.EqualTo("1 day 02:03:04"));

            Assert.That(new OpenGaussTimeSpan(1, 2, 3, 4, 5).ToString(), Is.EqualTo("1 day 02:03:04.005"));

            Assert.That(new OpenGaussTimeSpan(1, 2, 3, 4, 5, 6).ToString(), Is.EqualTo("1 mon 2 days 03:04:05.006"));

            Assert.That(new OpenGaussTimeSpan(1, 2, 3, 4, 5, 6, 7).ToString(), Is.EqualTo("14 mons 3 days 04:05:06.007"));

            Assert.That(new OpenGaussTimeSpan(new TimeSpan(0, 2, 3, 4, 5)).ToString(), Is.EqualTo(new OpenGaussTimeSpan(0, 2, 3, 4, 5).ToString()));

            Assert.That(new OpenGaussTimeSpan(new TimeSpan(1, 2, 3, 4, 5)).ToString(), Is.EqualTo(new OpenGaussTimeSpan(1, 2, 3, 4, 5).ToString()));
            const long moreThanAMonthInTicks = TimeSpan.TicksPerDay * 40;
            Assert.That(new OpenGaussTimeSpan(new TimeSpan(moreThanAMonthInTicks)).ToString(), Is.EqualTo(new OpenGaussTimeSpan(moreThanAMonthInTicks).ToString()));

            var testCulture = new CultureInfo("fr-FR");
            Assert.That(testCulture.NumberFormat.NumberDecimalSeparator, Is.EqualTo(","), "decimal seperator");
            using (TestUtil.SetCurrentCulture(testCulture))
            {
                Assert.That(new OpenGaussTimeSpan(1, 2, 3, 4, 5, 6, 7).ToString(), Is.EqualTo("14 mons 3 days 04:05:06.007"));
            }
        }

        [Test]
        public void OpenGaussDateConstructors()
        {
            OpenGaussDate date;
            DateTime dateTime;
            System.Globalization.Calendar calendar = new System.Globalization.GregorianCalendar();

            date = new OpenGaussDate();
            Assert.That(date.Day, Is.EqualTo(1));
            Assert.That(date.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
            Assert.That(date.DayOfYear, Is.EqualTo(1));
            Assert.That(date.IsLeapYear, Is.EqualTo(false));
            Assert.That(date.Month, Is.EqualTo(1));
            Assert.That(date.Year, Is.EqualTo(1));

            dateTime = new DateTime(2009, 5, 31);
            date = new OpenGaussDate(dateTime);
            Assert.That(date.Day, Is.EqualTo(dateTime.Day));
            Assert.That(date.DayOfWeek, Is.EqualTo(dateTime.DayOfWeek));
            Assert.That(date.DayOfYear, Is.EqualTo(dateTime.DayOfYear));
            Assert.That(date.IsLeapYear, Is.EqualTo(calendar.IsLeapYear(2009)));
            Assert.That(date.Month, Is.EqualTo(dateTime.Month));
            Assert.That(date.Year, Is.EqualTo(dateTime.Year));

            //Console.WriteLine(new DateTime(2009, 5, 31).Ticks);
            //Console.WriteLine((new DateTime(2009, 5, 31) - new DateTime(1, 1, 1)).TotalDays);
            // 2009-5-31
            dateTime = new DateTime(633793248000000000); // ticks since 1 Jan 1
            date = new OpenGaussDate(733557); // days since 1 Jan 1
            Assert.That(date.Day, Is.EqualTo(dateTime.Day));
            Assert.That(date.DayOfWeek, Is.EqualTo(dateTime.DayOfWeek));
            Assert.That(date.DayOfYear, Is.EqualTo(dateTime.DayOfYear));
            Assert.That(date.IsLeapYear, Is.EqualTo(calendar.IsLeapYear(2009)));
            Assert.That(date.Month, Is.EqualTo(dateTime.Month));
            Assert.That(date.Year, Is.EqualTo(dateTime.Year));

            // copy previous value.  should get same result
            date = new OpenGaussDate(date);
            Assert.That(date.Day, Is.EqualTo(dateTime.Day));
            Assert.That(date.DayOfWeek, Is.EqualTo(dateTime.DayOfWeek));
            Assert.That(date.DayOfYear, Is.EqualTo(dateTime.DayOfYear));
            Assert.That(date.IsLeapYear, Is.EqualTo(calendar.IsLeapYear(2009)));
            Assert.That(date.Month, Is.EqualTo(dateTime.Month));
            Assert.That(date.Year, Is.EqualTo(dateTime.Year));

#if NET6_0_OR_GREATER
            date = new OpenGaussDate(new DateOnly(2012, 3, 4));
            Assert.That(date.Year, Is.EqualTo(2012));
            Assert.That(date.Month, Is.EqualTo(3));
            Assert.That(date.Day, Is.EqualTo(4));
#endif
        }

        [Test]
        public void OpenGaussDateToString()
        {
            Assert.That(new OpenGaussDate(2009, 5, 31).ToString(), Is.EqualTo("2009-05-31"));

            Assert.That(new OpenGaussDate(-1, 5, 7).ToString(), Is.EqualTo("0001-05-07 BC"));

            var testCulture = new CultureInfo("fr-FR");
            Assert.That(testCulture.NumberFormat.NumberDecimalSeparator, Is.EqualTo(","), "decimal seperator");
            using (TestUtil.SetCurrentCulture(testCulture))
                Assert.That(new OpenGaussDate(2009, 5, 31).ToString(), Is.EqualTo("2009-05-31"));
        }

        [Test]
        public void SpecialDates()
        {
            OpenGaussDate date;
            DateTime dateTime;
            System.Globalization.Calendar calendar = new System.Globalization.GregorianCalendar();

            // a date after a leap year.
            dateTime = new DateTime(2008, 5, 31);
            date = new OpenGaussDate(dateTime);
            Assert.That(date.Day, Is.EqualTo(dateTime.Day));
            Assert.That(date.DayOfWeek, Is.EqualTo(dateTime.DayOfWeek));
            Assert.That(date.DayOfYear, Is.EqualTo(dateTime.DayOfYear));
            Assert.That(date.IsLeapYear, Is.EqualTo(calendar.IsLeapYear(2008)));
            Assert.That(date.Month, Is.EqualTo(dateTime.Month));
            Assert.That(date.Year, Is.EqualTo(dateTime.Year));

            // A date that is a leap year day.
            dateTime = new DateTime(2000, 2, 29);
            date = new OpenGaussDate(2000, 2, 29);
            Assert.That(date.Day, Is.EqualTo(dateTime.Day));
            Assert.That(date.DayOfWeek, Is.EqualTo(dateTime.DayOfWeek));
            Assert.That(date.DayOfYear, Is.EqualTo(dateTime.DayOfYear));
            Assert.That(date.IsLeapYear, Is.EqualTo(calendar.IsLeapYear(2000)));
            Assert.That(date.Month, Is.EqualTo(dateTime.Month));
            Assert.That(date.Year, Is.EqualTo(dateTime.Year));

            // A date that is not in a leap year.
            dateTime = new DateTime(1900, 3, 1);
            date = new OpenGaussDate(1900, 3, 1);
            Assert.That(date.Day, Is.EqualTo(dateTime.Day));
            Assert.That(date.DayOfWeek, Is.EqualTo(dateTime.DayOfWeek));
            Assert.That(date.DayOfYear, Is.EqualTo(dateTime.DayOfYear));
            Assert.That(date.IsLeapYear, Is.EqualTo(calendar.IsLeapYear(1900)));
            Assert.That(date.Month, Is.EqualTo(dateTime.Month));
            Assert.That(date.Year, Is.EqualTo(dateTime.Year));

            // a date after a leap year.
            date = new OpenGaussDate(-1, 12, 31);
            Assert.That(date.Day, Is.EqualTo(31));
            Assert.That(date.DayOfWeek, Is.EqualTo(DayOfWeek.Sunday));
            Assert.That(date.DayOfYear, Is.EqualTo(366));
            Assert.That(date.IsLeapYear, Is.EqualTo(true));
            Assert.That(date.Month, Is.EqualTo(12));
            Assert.That(date.Year, Is.EqualTo(-1));
        }

        [Test]
        public void OpenGaussDateMath()
        {
            OpenGaussDate date;

            // add a day to the empty constructor
            date = new OpenGaussDate() + new OpenGaussTimeSpan(0, 1, 0);
            Assert.That(date.Day, Is.EqualTo(2));
            Assert.That(date.DayOfWeek, Is.EqualTo(DayOfWeek.Tuesday));
            Assert.That(date.DayOfYear, Is.EqualTo(2));
            Assert.That(date.IsLeapYear, Is.EqualTo(false));
            Assert.That(date.Month, Is.EqualTo(1));
            Assert.That(date.Year, Is.EqualTo(1));

            // add a day the same value as the empty constructor
            date = new OpenGaussDate(1, 1, 1) + new OpenGaussTimeSpan(0, 1, 0);
            Assert.That(date.Day, Is.EqualTo(2));
            Assert.That(date.DayOfWeek, Is.EqualTo(DayOfWeek.Tuesday));
            Assert.That(date.DayOfYear, Is.EqualTo(2));
            Assert.That(date.IsLeapYear, Is.EqualTo(false));
            Assert.That(date.Month, Is.EqualTo(1));
            Assert.That(date.Year, Is.EqualTo(1));

            var diff = new OpenGaussDate(1, 1, 1) - new OpenGaussDate(-1, 12, 31);
            Assert.That(diff, Is.EqualTo(new OpenGaussTimeSpan(0, 1, 0)));

            // Test of the addMonths method (positive values added)
            var dateForTestMonths = new OpenGaussDate(2008, 1, 1);
            Assert.That(dateForTestMonths, Is.EqualTo(dateForTestMonths.AddMonths(0)));
            Assert.That(new OpenGaussDate(2008, 5, 1), Is.EqualTo(dateForTestMonths.AddMonths(4)));
            Assert.That(new OpenGaussDate(2008, 12, 1), Is.EqualTo(dateForTestMonths.AddMonths(11)));
            Assert.That(new OpenGaussDate(2009, 1, 1), Is.EqualTo(dateForTestMonths.AddMonths(12)));
            Assert.That(new OpenGaussDate(2009, 3, 1), Is.EqualTo(dateForTestMonths.AddMonths(14)));
            dateForTestMonths = new OpenGaussDate(2008, 1, 31);
            Assert.That(new OpenGaussDate(2008, 2, 29), Is.EqualTo(dateForTestMonths.AddMonths(1)));
            Assert.That(new OpenGaussDate(2009, 2, 28), Is.EqualTo(dateForTestMonths.AddMonths(13)));

            // Test of the addMonths method (negative values added)
            dateForTestMonths = new OpenGaussDate(2009, 1, 1);
            Assert.That(dateForTestMonths, Is.EqualTo(dateForTestMonths.AddMonths(0)));
            Assert.That(new OpenGaussDate(2008, 9, 1), Is.EqualTo(dateForTestMonths.AddMonths(-4)));
            Assert.That(new OpenGaussDate(2008, 1, 1), Is.EqualTo(dateForTestMonths.AddMonths(-12)));
            Assert.That(new OpenGaussDate(2007, 12, 1), Is.EqualTo(dateForTestMonths.AddMonths(-13)));
            dateForTestMonths = new OpenGaussDate(2009, 3, 31);
            Assert.That(new OpenGaussDate(2009, 2, 28), Is.EqualTo(dateForTestMonths.AddMonths(-1)));
            Assert.That(new OpenGaussDate(2008, 2, 29), Is.EqualTo(dateForTestMonths.AddMonths(-13)));
        }

        [Test, IssueLink("https://github.com/opengauss/opengauss/issues/3019")]
        public void OpenGaussDateTimeMath()
        {
            // Note* OpenGaussTimespan treats 1 month as 30 days
            Assert.That(new OpenGaussDateTime(2020, 1, 1, 0, 0, 0).Add(new OpenGaussTimeSpan(1, 2, 0)),
                Is.EqualTo(new OpenGaussDateTime(2020, 2, 2, 0, 0, 0)));
            Assert.That(new OpenGaussDateTime(2020, 1, 1, 0, 0, 0).Add(new OpenGaussTimeSpan(0, -1, 0)),
                Is.EqualTo(new OpenGaussDateTime(2019, 12, 31, 0, 0, 0)));
            Assert.That(new OpenGaussDateTime(2020, 1, 1, 0, 0, 0).Add(new OpenGaussTimeSpan(0, 0, 0)),
                Is.EqualTo(new OpenGaussDateTime(2020, 1, 1, 0, 0, 0)));
            Assert.That(new OpenGaussDateTime(2020, 1, 1, 0, 0, 0).Add(new OpenGaussTimeSpan(0, 0, 10000000)),
                Is.EqualTo(new OpenGaussDateTime(2020, 1, 1, 0, 0, 1)));
            Assert.That(new OpenGaussDateTime(2020, 1, 1, 0, 0, 0).Subtract(new OpenGaussTimeSpan(1, 1, 0)),
                Is.EqualTo(new OpenGaussDateTime(2019, 12, 1, 0, 0, 0)));
            // Add 1 month = 2020-03-01 then add 30 days (1 month in opengaussTimespan = 30 days) = 2020-03-31
            Assert.That(new OpenGaussDateTime(2020, 2, 1, 0, 0, 0).AddMonths(1).Add(new OpenGaussTimeSpan(1, 0, 0)),
                Is.EqualTo(new OpenGaussDateTime(2020, 3, 31, 0, 0, 0)));
        }

        [Test]
        public void TsVector()
        {
            OpenGaussTsVector vec;

            vec = OpenGaussTsVector.Parse("a");
            Assert.That(vec.ToString(), Is.EqualTo("'a'"));

            vec = OpenGaussTsVector.Parse("a ");
            Assert.That(vec.ToString(), Is.EqualTo("'a'"));

            vec = OpenGaussTsVector.Parse("a:1A");
            Assert.That(vec.ToString(), Is.EqualTo("'a':1A"));

            vec = OpenGaussTsVector.Parse(@"\abc\def:1a ");
            Assert.That(vec.ToString(), Is.EqualTo("'abcdef':1A"));

            vec = OpenGaussTsVector.Parse(@"abc:3A 'abc' abc:4B 'hello''yo' 'meh\'\\':5");
            Assert.That(vec.ToString(), Is.EqualTo(@"'abc':3A,4B 'hello''yo' 'meh''\\':5"));

            vec = OpenGaussTsVector.Parse(" a:12345C  a:24D a:25B b c d 1 2 a:25A,26B,27,28");
            Assert.That(vec.ToString(), Is.EqualTo("'1' '2' 'a':24,25A,26B,27,28,12345C 'b' 'c' 'd'"));
        }

        [Test]
        public void TsQuery()
        {
            OpenGaussTsQuery query;

            query = new OpenGaussTsQueryLexeme("a", OpenGaussTsQueryLexeme.Weight.A | OpenGaussTsQueryLexeme.Weight.B);
            query = new OpenGaussTsQueryOr(query, query);
            query = new OpenGaussTsQueryOr(query, query);

            var str = query.ToString();

            query = OpenGaussTsQuery.Parse("a & b | c");
            Assert.That(query.ToString(), Is.EqualTo("'a' & 'b' | 'c'"));

            query = OpenGaussTsQuery.Parse("'a''':*ab&d:d&!c");
            Assert.That(query.ToString(), Is.EqualTo("'a''':*AB & 'd':D & !'c'"));

            query = OpenGaussTsQuery.Parse("(a & !(c | d)) & (!!a&b) | c | d | e");
            Assert.That(query.ToString(), Is.EqualTo("( ( 'a' & !( 'c' | 'd' ) & !( !'a' ) & 'b' | 'c' ) | 'd' ) | 'e'"));
            Assert.That(OpenGaussTsQuery.Parse(query.ToString()).ToString(), Is.EqualTo(query.ToString()));

            query = OpenGaussTsQuery.Parse("(((a:*)))");
            Assert.That(query.ToString(), Is.EqualTo("'a':*"));

            query = OpenGaussTsQuery.Parse(@"'a\\b''cde'");
            Assert.That(((OpenGaussTsQueryLexeme)query).Text, Is.EqualTo(@"a\b'cde"));
            Assert.That(query.ToString(), Is.EqualTo(@"'a\\b''cde'"));

            query = OpenGaussTsQuery.Parse(@"a <-> b");
            Assert.That(query.ToString(), Is.EqualTo("'a' <-> 'b'"));

            query = OpenGaussTsQuery.Parse("((a & b) <5> c) <-> !d <0> e");
            Assert.That(query.ToString(), Is.EqualTo("( ( 'a' & 'b' <5> 'c' ) <-> !'d' ) <0> 'e'"));

            Assert.Throws(typeof(FormatException), () => OpenGaussTsQuery.Parse("a b c & &"));
            Assert.Throws(typeof(FormatException), () => OpenGaussTsQuery.Parse("&"));
            Assert.Throws(typeof(FormatException), () => OpenGaussTsQuery.Parse("|"));
            Assert.Throws(typeof(FormatException), () => OpenGaussTsQuery.Parse("!"));
            Assert.Throws(typeof(FormatException), () => OpenGaussTsQuery.Parse("("));
            Assert.Throws(typeof(FormatException), () => OpenGaussTsQuery.Parse(")"));
            Assert.Throws(typeof(FormatException), () => OpenGaussTsQuery.Parse("()"));
            Assert.Throws(typeof(FormatException), () => OpenGaussTsQuery.Parse("<"));
            Assert.Throws(typeof(FormatException), () => OpenGaussTsQuery.Parse("<-"));
            Assert.Throws(typeof(FormatException), () => OpenGaussTsQuery.Parse("<->"));
            Assert.Throws(typeof(FormatException), () => OpenGaussTsQuery.Parse("a <->"));
            Assert.Throws(typeof(FormatException), () => OpenGaussTsQuery.Parse("<>"));
            Assert.Throws(typeof(FormatException), () => OpenGaussTsQuery.Parse("a <a> b"));
            Assert.Throws(typeof(FormatException), () => OpenGaussTsQuery.Parse("a <-1> b"));
        }

        [Test]
        public void TsQueryEquatibility()
        {
            //Debugger.Launch();
            AreEqual(
                new OpenGaussTsQueryLexeme("lexeme"),
                new OpenGaussTsQueryLexeme("lexeme"));

            AreEqual(
                new OpenGaussTsQueryLexeme("lexeme", OpenGaussTsQueryLexeme.Weight.A | OpenGaussTsQueryLexeme.Weight.B),
                new OpenGaussTsQueryLexeme("lexeme", OpenGaussTsQueryLexeme.Weight.A | OpenGaussTsQueryLexeme.Weight.B));

            AreEqual(
                new OpenGaussTsQueryLexeme("lexeme", OpenGaussTsQueryLexeme.Weight.A | OpenGaussTsQueryLexeme.Weight.B, true),
                new OpenGaussTsQueryLexeme("lexeme", OpenGaussTsQueryLexeme.Weight.A | OpenGaussTsQueryLexeme.Weight.B, true));

            AreEqual(
                new OpenGaussTsQueryNot(new OpenGaussTsQueryLexeme("not")),
                new OpenGaussTsQueryNot(new OpenGaussTsQueryLexeme("not")));

            AreEqual(
                new OpenGaussTsQueryAnd(new OpenGaussTsQueryLexeme("left"), new OpenGaussTsQueryLexeme("right")),
                new OpenGaussTsQueryAnd(new OpenGaussTsQueryLexeme("left"), new OpenGaussTsQueryLexeme("right")));

            AreEqual(
                new OpenGaussTsQueryOr(new OpenGaussTsQueryLexeme("left"), new OpenGaussTsQueryLexeme("right")),
                new OpenGaussTsQueryOr(new OpenGaussTsQueryLexeme("left"), new OpenGaussTsQueryLexeme("right")));

            AreEqual(
                new OpenGaussTsQueryFollowedBy(new OpenGaussTsQueryLexeme("left"), 0, new OpenGaussTsQueryLexeme("right")),
                new OpenGaussTsQueryFollowedBy(new OpenGaussTsQueryLexeme("left"), 0, new OpenGaussTsQueryLexeme("right")));

            AreEqual(
                new OpenGaussTsQueryFollowedBy(new OpenGaussTsQueryLexeme("left"), 1, new OpenGaussTsQueryLexeme("right")),
                new OpenGaussTsQueryFollowedBy(new OpenGaussTsQueryLexeme("left"), 1, new OpenGaussTsQueryLexeme("right")));

            AreEqual(
                new OpenGaussTsQueryEmpty(),
                new OpenGaussTsQueryEmpty());

            AreNotEqual(
                new OpenGaussTsQueryLexeme("lexeme a"),
                new OpenGaussTsQueryLexeme("lexeme b"));

            AreNotEqual(
                new OpenGaussTsQueryLexeme("lexeme", OpenGaussTsQueryLexeme.Weight.A | OpenGaussTsQueryLexeme.Weight.D),
                new OpenGaussTsQueryLexeme("lexeme", OpenGaussTsQueryLexeme.Weight.A | OpenGaussTsQueryLexeme.Weight.B));

            AreNotEqual(
                new OpenGaussTsQueryLexeme("lexeme", OpenGaussTsQueryLexeme.Weight.A | OpenGaussTsQueryLexeme.Weight.B, true),
                new OpenGaussTsQueryLexeme("lexeme", OpenGaussTsQueryLexeme.Weight.A | OpenGaussTsQueryLexeme.Weight.B, false));

            AreNotEqual(
                new OpenGaussTsQueryNot(new OpenGaussTsQueryLexeme("not")),
                new OpenGaussTsQueryNot(new OpenGaussTsQueryLexeme("ton")));

            AreNotEqual(
                new OpenGaussTsQueryAnd(new OpenGaussTsQueryLexeme("right"), new OpenGaussTsQueryLexeme("left")),
                new OpenGaussTsQueryAnd(new OpenGaussTsQueryLexeme("left"), new OpenGaussTsQueryLexeme("right")));

            AreNotEqual(
                new OpenGaussTsQueryOr(new OpenGaussTsQueryLexeme("right"), new OpenGaussTsQueryLexeme("left")),
                new OpenGaussTsQueryOr(new OpenGaussTsQueryLexeme("left"), new OpenGaussTsQueryLexeme("right")));

            AreNotEqual(
                new OpenGaussTsQueryFollowedBy(new OpenGaussTsQueryLexeme("right"), 0, new OpenGaussTsQueryLexeme("left")),
                new OpenGaussTsQueryFollowedBy(new OpenGaussTsQueryLexeme("left"), 0, new OpenGaussTsQueryLexeme("right")));

            AreNotEqual(
                new OpenGaussTsQueryFollowedBy(new OpenGaussTsQueryLexeme("left"), 0, new OpenGaussTsQueryLexeme("right")),
                new OpenGaussTsQueryFollowedBy(new OpenGaussTsQueryLexeme("left"), 1, new OpenGaussTsQueryLexeme("right")));

            void AreEqual(OpenGaussTsQuery left, OpenGaussTsQuery right)
            {
                Assert.That(left == right, Is.True);
                Assert.That(left != right, Is.False);
                Assert.That(right, Is.EqualTo(left));
                Assert.That(right.GetHashCode(), Is.EqualTo(left.GetHashCode()));
            }

            void AreNotEqual(OpenGaussTsQuery left, OpenGaussTsQuery right)
            {
                Assert.That(left == right, Is.False);
                Assert.That(left != right, Is.True);
                Assert.That(right, Is.Not.EqualTo(left));
                Assert.That(right.GetHashCode(), Is.Not.EqualTo(left.GetHashCode()));
            }
        }

        [Test]
        public void TsQueryOperatorPrecedence()
        {
            var query = OpenGaussTsQuery.Parse("!a <-> b & c | d & e");
            var expectedGrouping = OpenGaussTsQuery.Parse("((!(a) <-> b) & c) | (d & e)");
            Assert.That(query.ToString(), Is.EqualTo(expectedGrouping.ToString()));
        }

        [Test]
        public void Bug1011018()
        {
            var p = new OpenGaussParameter();
            p.OpenGaussDbType = OpenGaussDbType.Time;
            p.Value = DateTime.Now;
            var o = p.Value;
        }

#pragma warning disable 618
        [Test]
        [IssueLink("https://github.com/opengauss/opengauss/issues/750")]
        public void OpenGaussInet()
        {
            var v = new OpenGaussInet(IPAddress.Parse("2001:1db8:85a3:1142:1000:8a2e:1370:7334"), 32);
            Assert.That(v.ToString(), Is.EqualTo("2001:1db8:85a3:1142:1000:8a2e:1370:7334/32"));

#pragma warning disable CS8625
            Assert.That(v != null);  // #776
#pragma warning disable CS8625
        }
#pragma warning restore 618
    }
}
