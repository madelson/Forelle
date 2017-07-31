using Forelle.Parsing.Construction;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Tests.Parsing.Construction
{
    public class ImmutableTrieTest
    {
        [Test]
        public void TestImmutableTrie()
        {
            var trie = ImmutableTrie<char, char[]>.Empty;

            trie = trie.Add("abcd", "abcd".ToCharArray()).ShouldNotEqual(trie);
            trie = trie.Add("abc", "abc".ToCharArray()).ShouldNotEqual(trie);
            var bcdCharArray = "bcd".ToCharArray();
            trie = trie.Add("bcd", bcdCharArray).ShouldNotEqual(trie);
            trie.Add("bcd", bcdCharArray).ShouldEqual(trie, "should already be present");
            trie = trie.Add("bcd", new char[0]).ShouldNotEqual(trie, "can add multiple values for a key");

            trie["abcde"].IsEmpty.ShouldEqual(true);
            CollectionAssert.AreEqual(actual: trie["abcd"].Single(), expected: "abcd");
            CollectionAssert.AreEqual(actual: trie["abc"].Single(), expected: "abc");
            var bcdValue = trie["bcd"];
            bcdValue.Count.ShouldEqual(2);
            bcdValue.Count(a => a.Length == 0).ShouldEqual(1);
            bcdValue.Count(a => a.SequenceEqual("bcd")).ShouldEqual(1);

            var prefixValueResult = trie.GetWithPrefixValues("abcd");
            prefixValueResult.Count.ShouldEqual(2);
            prefixValueResult.Count(a => a.SequenceEqual("abcd")).ShouldEqual(1);
            prefixValueResult.Count(a => a.SequenceEqual("abc")).ShouldEqual(1);
        }
    }
}
