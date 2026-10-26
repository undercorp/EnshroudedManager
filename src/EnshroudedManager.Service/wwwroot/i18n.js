"use strict";
// French is the source language: every text is written in French in the page and in app.js,
// and looked up here when the service language is English. {0}, {1}… are placeholders.

let lang = "fr";

const EN = {
  // page
  "Tableau de bord": "Dashboard", "Paramètres": "Settings", "Sauvegardes": "Backups", "Journal": "Log", "Outil": "Tool",
  "État": "Status", "Joueurs": "Players", "Version installée": "Installed version", "Mises à jour": "Updates",
  "Actions": "Actions", "Démarrer": "Start", "Arrêter": "Stop", "Redémarrer": "Restart",
  "Vérifier les mises à jour": "Check for updates", "Mettre à jour maintenant": "Update now", "Sauvegarder maintenant": "Back up now",
  "Arrêter le serveur ? Les joueurs connectés seront déconnectés.": "Stop the server? Connected players will be disconnected.",
  "Redémarrer le serveur ? Les joueurs connectés seront déconnectés.": "Restart the server? Connected players will be disconnected.",
  "Forcer la mise à jour maintenant ? Le serveur sera arrêté, sauvegardé, mis à jour (validation des fichiers) puis relancé.":
    "Force an update now? The server will be stopped, backed up, updated (file validation) and restarted.",
  "Derniers événements": "Recent events",
  "Les paramètres ont été modifiés ailleurs (application bureau ou autre navigateur).": "Settings were changed elsewhere (desktop app or another browser).",
  "Recharger": "Reload", "Modifications non enregistrées": "Unsaved changes", "Annuler": "Cancel", "Appliquer et redémarrer": "Apply and restart",
  "Sauvegardes du monde": "World backups",
  "Une sauvegarde est faite automatiquement avant chaque mise à jour, chaque modification des paramètres et chaque restauration. Chaque archive contient le dossier savegame et enshrouded_server.json.":
    "A backup is made automatically before every update, every settings change and every restore. Each archive holds the savegame folder and enshrouded_server.json.",
  "Date": "Date", "Motif": "Reason", "Taille": "Size",
  "Journal de l'outil": "Tool log", "Réglages de l'outil": "Tool settings", "Mise à jour automatique": "Automatic updates",
  "Vérifier toutes les (minutes)": "Check every (minutes)", "Relancer le serveur s'il plante": "Restart the server if it crashes",
  "Nombre de sauvegardes à garder": "Backups to keep", "Langue": "Language", "Enregistrer": "Save",
  "Mot de passe de l'interface": "Interface password", "Mot de passe actuel": "Current password", "Nouveau mot de passe": "New password",
  "Changer": "Change", "Se déconnecter": "Sign out", "Confirmer": "Confirm", "Mot de passe": "Password",
  "Confirmer le mot de passe": "Confirm password", "Se connecter": "Sign in",
  "Connexion à l'outil de gestion du serveur.": "Sign in to the server management tool.",
  // app.js
  "Session expirée": "Session expired", "Erreur {0}": "Error {0}", "{0} j {1} h": "{0} d {1} h",
  "Première connexion : choisis le mot de passe qui protégera l'interface (8 caractères minimum).":
    "First sign-in: choose the password that will protect the interface (at least 8 characters).",
  "Définir le mot de passe": "Set the password", "Les deux mots de passe sont différents.": "The two passwords do not match.",
  "Depuis {0}": "For {0}", "Mémoire : {0} Go": "Memory: {0} GB", "Nouvelle version disponible : {0}": "New version available: {0}",
  "À jour avec Steam": "Up to date with Steam", "Version Steam pas encore vérifiée": "Steam version not checked yet",
  "Automatiques": "Automatic", "Manuelles": "Manual", "Vérifié : {0} · prochaine : {1}": "Checked: {0} · next: {1}",
  " · dernière MAJ : {0}": " · last update: {0}", "En cours…": "Working…",
  "Plage officielle : {0} – {1}": "Official range: {0} – {1}",
  " — valeur actuelle hors plage (accepté par le jeu, mais non garanti)": " — current value out of range (accepted by the game, but not guaranteed)",
  "Voir": "Show", "Cacher": "Hide", "Groupes de joueurs": "Player groups",
  "Chaque groupe a son mot de passe : le joueur obtient les droits du groupe dont il saisit le mot de passe en rejoignant le serveur.":
    "Each group has its own password: players get the rights of the group whose password they enter when joining the server.",
  "Nouveau groupe": "New group", "Supprimer": "Delete", "Ajouter un groupe": "Add a group",
  "Paramètres invalides": "Invalid settings", "OK": "OK",
  "Le serveur va être arrêté, le monde et l'ancienne configuration sauvegardés, puis le serveur relancé avec les nouveaux paramètres. Les joueurs connectés seront déconnectés.":
    "The server will be stopped, the world and the previous settings backed up, then the server restarted with the new settings. Connected players will be disconnected.",
  "Avertissements :": "Warnings:", "Appliquer les paramètres ?": "Apply the settings?", "Échec": "Failed", "Erreur": "Error",
  "Avant mise à jour": "Before update", "Avant changement de paramètres": "Before settings change", "Avant restauration": "Before restore",
  "Manuelle": "Manual", " Mo": " MB", "Télécharger": "Download", "Restaurer": "Restore",
  "Aucune sauvegarde pour l'instant.": "No backup yet.", "Restaurer cette sauvegarde ?": "Restore this backup?",
  "Le monde sera remplacé par celui du {0}. L'état actuel est sauvegardé juste avant, au cas où.":
    "The world will be replaced by the one from {0}. The current state is backed up first, just in case.",
  "Restaurer aussi les paramètres (enshrouded_server.json)": "Also restore the settings (enshrouded_server.json)",
  "Mot de passe changé. Les autres sessions (navigateurs, application bureau) sont déconnectées.":
    "Password changed. Other sessions (browsers, desktop app) have been signed out.",
  "Le serveur n'est pas encore installé : les paramètres seront disponibles à la fin du téléchargement.":
    "The server is not installed yet: settings will be available once the download is finished.",
};

function t(fr, ...args) {
  let s = lang === "en" && EN[fr] !== undefined ? EN[fr] : fr;
  args.forEach((a, i) => { s = s.replaceAll(`{${i}}`, a); });
  return s;
}

/** Translates the static page: text nodes and the confirm messages of the action buttons. */
function translateDom(root = document.body) {
  document.documentElement.lang = lang;
  if (lang !== "en") return;
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  for (let n = walker.nextNode(); n; n = walker.nextNode()) {
    const key = n.nodeValue.trim();
    if (key && EN[key] !== undefined) n.nodeValue = n.nodeValue.replace(key, EN[key]);
  }
  root.querySelectorAll("[data-confirm]").forEach(e => { e.dataset.confirm = t(e.dataset.confirm); });
}
