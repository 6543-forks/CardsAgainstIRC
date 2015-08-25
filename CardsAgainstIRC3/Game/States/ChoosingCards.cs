﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CardsAgainstIRC3.Game.States
{

    public class ChoosingCards : Base
    {
        public ChoosingCards(GameManager manager)
            : base(manager, 60)
        { }

        public List<GameUser> WaitingOnUsers = new List<GameUser>();
        public List<GameUser> ChosenUsers = new List<GameUser>();

        public override void Activate()
        {
            foreach (var person in Manager.AllUsers.Where(a => a.WantsToLeave).Select(a => a.Nick).ToArray())
            {
                Manager.UserQuit(person);
            }

            Manager.UpdateCzars();

            var czar = Manager.NextCzar();
            Manager.NewBlackCard();

            if (Manager.AllUsers.Count() < 3)
            {
                Manager.SendToAll("Not enough players, stopping game");
                Manager.Reset();
                return;
            }

            foreach (var person in Manager.AllUsers)
            {
                person.HasChosenCards = person.HasVoted = false;

                if (person.Bot == null && person.CanChooseCards && (person != czar || Manager.Mode == GameManager.GameMode.SovietRussia))
                    WaitingOnUsers.Add(person);
            }

            if (Manager.Mode == GameManager.GameMode.Czar)
                Manager.SendToAll("New round! {0} is czar! {1}, choose your cards!", czar.Nick, string.Join(", ", WaitingOnUsers.Select(a => a.Nick)));
            else
                Manager.SendToAll("New round! {0}, choose your cards!", string.Join(", ", WaitingOnUsers.Select(a => a.Nick)));
            Manager.SendToAll("Current Card: {0}", Manager.CurrentBlackCard.Representation());

            foreach (var user in WaitingOnUsers)
            {
                user.UpdateCards();
                user.SendCards();
            }

            CheckReady();
        }

        public override bool UserLeft(GameUser user, bool voluntarily)
        {

            if (!voluntarily)
                user.CanChooseCards = user.CanVote = false;
            else
                user.CanVote = false;

            if (WaitingOnUsers.Contains(user))
            {
                WaitingOnUsers.Remove(user);
                ChosenUsers.Remove(user);
                user.HasChosenCards = false;
                CheckReady();
            }

            if (user == Manager.CurrentCzar() && Manager.Mode == GameManager.GameMode.Czar)
            {
                Manager.SendToAll("Czar left! Discarding round...");
                foreach(var person in ChosenUsers)
                    person.ChosenCards = new int[] { };
                Manager.StartState(new ChoosingCards(Manager));
            }

            return true;
        }

        public override void TimeoutReached()
        {
            Manager.SendToAll("Timeout reached! {0} - 1 point", string.Join(", ", WaitingOnUsers.Select(a => a.Nick)));

            foreach (var person in WaitingOnUsers)
                person.Points--;

            Manager.StartState(new VoteForCards(Manager));
        }

        private void CheckReady()
        {
            if (WaitingOnUsers.Count == 0)
            {
                Manager.StartState(new VoteForCards(Manager));
            }
        }

        [Command("!cards")]
        public void CardsCommand(string nick, IEnumerable<string> arguments)
        {
            var user = Manager.Resolve(nick);
            if (user == null)
                return;

            user.SendCards();
        }

        [Command("!card", "!pick", "!p")]
        public void CardCommand(string nick, IEnumerable<string> arguments)
        {
            var user = Manager.Resolve(nick);
            if (user == null)
                return;
            if (!WaitingOnUsers.Contains(user) && !ChosenUsers.Contains(user))
            {
                Manager.SendPrivate(user, "You don't have to choose cards!");
                return;
            }

            if (arguments.Count() == 0 && Manager.CurrentBlackCard.Parts.Length != 1)
            {
                user.SendCards();
                return;
            }

            int[] cards = new int[] { };
            try
            {
                cards = arguments.Select(a => int.Parse(a)).ToArray();
            }
            catch (FormatException)
            {
                Manager.SendPrivate(user, "That's not an int!");
                return;
            }
            catch (OverflowException)
            {
                Manager.SendPrivate(user, "Sorry, but you don't have that much cards!");
                return;
            }

            if (cards.Length > 0 && (cards.Min() < 0 || cards.Max() > user.Cards.Length || cards.Any(a => !user.Cards[a].HasValue)))
            {
                Manager.SendPrivate(user, "Invalid cards!");
                return;
            }

            if (cards.Length > 0 && cards.GroupBy(a => a).Any(a => a.Count() > 1))
            {
                Manager.SendPrivate(user, "You can't use duplicates!");
                return;
            }

            if (cards.Length != Manager.CurrentBlackCard.Parts.Length - 1)
            {
                Manager.SendPrivate(user, "You haven't chosen enough cards!");
                return;
            }

            user.ChosenCards = cards;
            user.HasChosenCards = true;

            Manager.SendPrivate(user, "You have chosen: {0}", Manager.CurrentBlackCard.Representation(user.ChosenCards.Select(a => user.Cards[a].Value)));

            if (WaitingOnUsers.Contains(user))
            {
                WaitingOnUsers.Remove(user);
                ChosenUsers.Add(user);
                CheckReady();
            }
        }

        [Command("!status")]
        public void StatusCommand(string nick, IEnumerable<string> arguments)
        {
            Manager.SendPublic(nick, "Waiting for {0} to choose", string.Join(", ", WaitingOnUsers.Select(a => a.Nick)));
        }

        [Command("!skip")]
        public void SkipCommand(string nick, IEnumerable<string> arguments)
        {
            var user = Manager.Resolve(nick);
            if (user == null || (!WaitingOnUsers.Contains(user) && !ChosenUsers.Contains(user)))
                return;

            user.HasChosenCards = false;
            if (WaitingOnUsers.Contains(user))
            {
                WaitingOnUsers.Remove(user);
                ChosenUsers.Add(user);
                CheckReady();
            }
        }
    }
}
