// corrigé le 25/06/2020
// =====================================================================================
// EMP_Multibande_NUMERAL SYSTEMS_BaseX
// Morphologie mathématique multivaluée (images multibandes) avec ordonnancement vectoriel
// fondé sur les SYSTÈMES DE NUMÉRATION.
//
// Chaque pixel-vecteur V = (v1, v2, ..., vm) est vu comme un nombre écrit en base X
// (X = 256 pour des images 8 bits, toutes les composantes étant < X) :
//     Num(V) = v1*X^(m-1) + v2*X^(m-2) + ... + v(m-1)*X + vm
// La bande 1 (première image chargée) porte le chiffre de poids le plus fort.
// Cette conversion est injective : deux vecteurs distincts ont deux valeurs Num distinctes.
// L'ordre V < V' <=> Num(V) < Num(V') est un ordre total :
//     infimum  (érosion)    = pixel-vecteur de Num minimal du voisinage ;
//     supremum (dilatation) = pixel-vecteur de Num maximal du voisinage.
//
// Transformations calculées pour chaque taille i = 1..max de l'élément structurant (ES) :
//   érosion, dilatation, ouverture, fermeture, ouverture par reconstruction,
//   fermeture par reconstruction (+ export multibande ENVI .hdr).
//
// ES : disque. Le disque de taille i est obtenu par i itérations de l'ES de base B1
//      (disque de rayon 1 = 5 pixels), par associativité de la somme de Minkowski.
// =====================================================================================
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows;
using SDColor = System.Drawing.Color;
using SDPoint = System.Drawing.Point;

namespace EMP_Multibande_NUMERAL_SYSTEMS_BaseX
{
    public partial class MainWindow : Window
    {
        // Base X du système de numération : 256 niveaux pour des images codées sur 8 bits
        private const int BASE_X = 256;

        // Bandes chargées par l'utilisateur (une image panchromatique = une bande)
        List<Bitmap> imagesBmp = new List<Bitmap>();

        public MainWindow()
        {
            InitializeComponent();
        }

        // Dossier de sortie « IMAGES-résultat » : toujours juste sous le dossier du projet.
        // On remonte depuis le dossier de l'exécutable (bin\Debug\... ou Executable\) jusqu'au
        // dossier qui contient la solution (.sln) ; s'il n'y en a pas (exécutable copié seul sur
        // une autre machine), le dossier est créé à côté de l'exécutable.
        private static string DossierResultats()
        {
            string dossierExe = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                DirectoryInfo d = new DirectoryInfo(dossierExe);
                while (d != null)
                {
                    if (d.GetFiles("*.sln").Length > 0)
                        return Path.Combine(d.FullName, "IMAGES-résultat");
                    d = d.Parent;
                }
            }
            catch (Exception)
            {
                // Dossier parent illisible : on garde le dossier de l'exécutable
            }
            return Path.Combine(dossierExe, "IMAGES-résultat");
        }

        // ============================== CHARGEMENT DES BANDES ==============================
        // L'ordre des bandes est celui des fichiers renvoyés par la boîte de dialogue :
        // la première bande porte le chiffre de poids le plus fort dans Num(V).
        private void button_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFile = new OpenFileDialog();
            openFile.Multiselect = true;
            openFile.DefaultExt = "png";
            openFile.Filter = "PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|BMP (*.bmp)|*.bmp|TIFF (*.tiff;*.tif)|*.tiff;*.tif";
            bool? ok = openFile.ShowDialog();
            if (ok != true || openFile.FileNames.Length == 0)
                return;
            // On libère les anciennes bandes avant d'en charger de nouvelles
            foreach (Bitmap old in imagesBmp)
                old.Dispose();
            imagesBmp.Clear();
            foreach (string filename in openFile.FileNames)
                imagesBmp.Add(new Bitmap(filename));
            MessageBox.Show(imagesBmp.Count + " bande(s) chargée(s).", "Chargement");
        }

        // Bouton de lancement de l'interface d'origine (Button_Click_1) et de l'interface de référence (button1_Click)
        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            button1_Click(sender, e);
        }

        // Lecture sécurisée d'un entier
        private static bool TryParseInt(string s, out int v)
        {
            return int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
                || int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out v);
        }

        // Lecture sécurisée d'un réel (accepte la virgule ou le point décimal)
        private static bool TryParseDouble(string s, out double v)
        {
            string t = (s ?? "").Trim().Replace(',', '.');
            return double.TryParse(t, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out v);
        }

        // Lit le texte de la première zone de texte trouvée parmi les noms donnés (null si aucune).
        // FindName évite toute erreur de compilation si un nom n'existe pas dans l'interface.
        private string LireTexte(params string[] noms)
        {
            foreach (string nom in noms)
            {
                var tb = FindName(nom) as System.Windows.Controls.TextBox;
                if (tb != null) return tb.Text;
            }
            return null;
        }

        // ============================== PROGRAMME PRINCIPAL ==============================
        private void button1_Click(object sender, RoutedEventArgs e)
        {
            // ---- 1. Vérification des données saisies ----
            if (imagesBmp.Count == 0)
            {
                MessageBox.Show("Chargez d'abord au moins une bande (bouton Parcourir).", "Erreur");
                return;
            }
            int max, itStabilite;
            double prStabilitePct;

            // Taille maximale de l'ES (zone « tailleMax » de l'interface d'origine, ou « textBox »)
            string sMax = LireTexte("tailleMax", "textBox");
            if (sMax == null)
            {
                MessageBox.Show("Zone de saisie de la taille de l'ES introuvable dans l'interface.", "Erreur");
                return;
            }
            if (!TryParseInt(sMax, out max) || max < 1)
            {
                MessageBox.Show("Taille maximale de l'ES invalide (entier >= 1).", "Erreur");
                return;
            }

            // Nombre maximal d'itérations de la reconstruction (0 = illimité).
            // Si l'interface ne contient pas cette zone, on prend 0 (jusqu'à stabilité).
            string sIt = LireTexte("textBox2");
            if (sIt == null) itStabilite = 0;
            else if (!TryParseInt(sIt, out itStabilite) || itStabilite < 0)
            {
                MessageBox.Show("Nombre d'itérations de stabilité invalide (entier >= 0 ; 0 = illimité).", "Erreur");
                return;
            }

            // Pourcentage de ressemblance entre deux images successives.
            // Si l'interface ne contient pas cette zone, on prend 100 % (stabilité exacte).
            string sPr = LireTexte("textBox3");
            if (sPr == null) prStabilitePct = 100;
            else if (!TryParseDouble(sPr, out prStabilitePct) || prStabilitePct < 0 || prStabilitePct > 100)
            {
                MessageBox.Show("Pourcentage de ressemblance invalide (0 à 100).", "Erreur");
                return;
            }
            double prStabilite = prStabilitePct / 100.0;

            // Toutes les bandes doivent avoir la même taille
            int w0 = imagesBmp[0].Width, h0 = imagesBmp[0].Height;
            for (int k = 1; k < imagesBmp.Count; k++)
            {
                if (imagesBmp[k].Width != w0 || imagesBmp[k].Height != h0)
                {
                    MessageBox.Show("Toutes les bandes doivent avoir la même taille.", "Erreur");
                    return;
                }
            }

            // ---- 2. Dossier de sortie ----
            string outDir = DossierResultats();
            Directory.CreateDirectory(outDir);

            // ---- 3. Conversion des bitmaps en matrices d'entiers [x, y] (une matrice par bande) ----
            List<int[,]> imagesMat;
            try
            {
                imagesMat = bmpToMat(imagesBmp);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible de lire les images : " + ex.Message, "Erreur");
                return;
            }

            // Condition du système de numération : toutes les composantes doivent être < X
            foreach (int[,] bande in imagesMat)
                foreach (int v in bande)
                    if (v < 0 || v >= BASE_X)
                    {
                        MessageBox.Show("Valeur de pixel hors de la base X = " + BASE_X + ".", "Erreur");
                        return;
                    }

            try
            {
                // Listes de toutes les reconstructions (toutes tailles d'ES, toutes bandes) pour l'export ENVI
                List<int[,]> enviOuvert = new List<int[,]>();
                List<int[,]> enviFerme = new List<int[,]>();
                List<string> nomsOuvert = new List<string>();
                List<string> nomsFerme = new List<string>();

                // ---- 4. Boucle sur la taille i de l'ES (disque) ----
                for (int i = 1; i <= max; i++)
                {
                    List<int[,]> imagesErodeInit = new List<int[,]>();
                    List<int[,]> imagesDilateInit = new List<int[,]>();
                    List<int[,]> imagesOuvertesStandards = new List<int[,]>();
                    List<int[,]> imagesFermeesStandards = new List<int[,]>();

                    // Érosion et dilatation multivaluées de taille i
                    ErosionDilatationInit(imagesMat, ref imagesErodeInit, ref imagesDilateInit, i);
                    // Ouverture et fermeture standard de taille i
                    OuvertureFermetureStandard(imagesErodeInit, imagesDilateInit, ref imagesOuvertesStandards, ref imagesFermeesStandards, i);

                    // Sauvegarde des résultats (une image TIFF par bande)
                    for (int k = 0; k < imagesBmp.Count; k++)
                    {
                        Bitmap sortieErod = new Bitmap(w0, h0);
                        Bitmap sortieDilat = new Bitmap(w0, h0);
                        Bitmap sortieOuverteStandard = new Bitmap(w0, h0);
                        Bitmap sortieFermeeStandard = new Bitmap(w0, h0);
                        for (int x = 0; x < w0; x++)
                            for (int y = 0; y < h0; y++)
                            {
                                sortieErod.SetPixel(x, y, Gray(imagesErodeInit[k][x, y]));
                                sortieDilat.SetPixel(x, y, Gray(imagesDilateInit[k][x, y]));
                                sortieOuverteStandard.SetPixel(x, y, Gray(imagesOuvertesStandards[k][x, y]));
                                sortieFermeeStandard.SetPixel(x, y, Gray(imagesFermeesStandards[k][x, y]));
                            }
                        sortieErod.Save(Path.Combine(outDir, "Erod B_" + k + " ES_" + i + ".tiff"));
                        sortieDilat.Save(Path.Combine(outDir, "Dilat B_" + k + " ES_" + i + ".tiff"));
                        sortieOuverteStandard.Save(Path.Combine(outDir, "OuvertureStandard B_" + k + " ES_" + i + ".tiff"));
                        sortieFermeeStandard.Save(Path.Combine(outDir, "FermetureStandard B_" + k + " ES_" + i + ".tiff"));
                        sortieErod.Dispose();
                        sortieDilat.Dispose();
                        sortieOuverteStandard.Dispose();
                        sortieFermeeStandard.Dispose();
                    }

                    // Ouverture et fermeture par reconstruction (arrêt par stabilité)
                    List<int[,]> gNewFerme = new List<int[,]>();
                    List<int[,]> gNewOuvert = new List<int[,]>();
                    Reconstruction(imagesMat, imagesErodeInit, imagesDilateInit, itStabilite, prStabilite, ref gNewOuvert, ref gNewFerme);

                    for (int k = 0; k < imagesBmp.Count; k++)
                    {
                        Bitmap sortieFerme = new Bitmap(w0, h0);
                        Bitmap sortieOuvert = new Bitmap(w0, h0);
                        for (int x = 0; x < w0; x++)
                            for (int y = 0; y < h0; y++)
                            {
                                sortieFerme.SetPixel(x, y, Gray(gNewFerme[k][x, y]));
                                sortieOuvert.SetPixel(x, y, Gray(gNewOuvert[k][x, y]));
                            }
                        sortieFerme.Save(Path.Combine(outDir, "FermeReconstruction B_" + k + " ES_" + i + ".tiff"));
                        sortieOuvert.Save(Path.Combine(outDir, "OuvertReconstruction B_" + k + " ES_" + i + ".tiff"));
                        sortieFerme.Dispose();
                        sortieOuvert.Dispose();
                        // Mémorisation pour les fichiers multibandes ENVI
                        enviOuvert.Add((int[,])gNewOuvert[k].Clone());
                        enviFerme.Add((int[,])gNewFerme[k].Clone());
                        nomsOuvert.Add("OuvertReconstruction B_" + k + " ES_" + i);
                        nomsFerme.Add("FermeReconstruction B_" + k + " ES_" + i);
                    }
                }

                // ---- 5. Fichiers multibandes ENVI (.img + .hdr) ----
                List<int[,]> enviTout = new List<int[,]>();
                List<string> nomsTout = new List<string>();
                enviTout.AddRange(enviOuvert);
                enviTout.AddRange(enviFerme);
                nomsTout.AddRange(nomsOuvert);
                nomsTout.AddRange(nomsFerme);
                WriteEnviMultiband(outDir, "EXTENDED-PROFIL-MOR-multibande", w0, h0, enviTout, nomsTout);
                WriteEnviMultiband(outDir, "OuvertReconstruction-multibande", w0, h0, enviOuvert, nomsOuvert);
                WriteEnviMultiband(outDir, "FermeReconstruction-multibande", w0, h0, enviFerme, nomsFerme);

                MessageBox.Show("Traitement terminé.\nRésultats (TIFF + ENVI .hdr) dans :\n" + outDir, "EMP_Multibande_NUMERAL SYSTEMS_BaseX");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur pendant le calcul :\n" + ex.Message, "Erreur");
            }
        }

        // Convertit une valeur entière en niveau de gris (borné à [0, 255])
        private static SDColor Gray(int v)
        {
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return SDColor.FromArgb(v, v, v);
        }

        // Écrit un fichier multibande ENVI (.img en BSQ, 8 bits + .hdr)
        private static void WriteEnviMultiband(string outDir, string baseName, int width, int height, List<int[,]> bands, List<string> bandNames)
        {
            if (bands == null || bands.Count == 0)
                return;
            string imgPath = Path.Combine(outDir, baseName + ".img");
            string hdrPath = Path.Combine(outDir, baseName + ".hdr");
            using (FileStream fs = new FileStream(imgPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buf = new byte[width * height];
                for (int b = 0; b < bands.Count; b++)
                {
                    int idx = 0;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int v = bands[b][x, y];
                            if (v < 0) v = 0;
                            if (v > 255) v = 255;
                            buf[idx++] = (byte)v;
                        }
                    }
                    fs.Write(buf, 0, buf.Length);
                }
            }
            var names = new System.Text.StringBuilder();
            for (int i = 0; i < bandNames.Count; i++)
            {
                if (i > 0) names.Append(",\r\n ");
                names.Append(bandNames[i]);
            }
            string hdr =
                "ENVI\r\n" +
                "description = { EMP_Multibande_NUMERAL SYSTEMS_BaseX, " + baseName + " }\r\n" +
                "samples = " + width + "\r\n" +
                "lines = " + height + "\r\n" +
                "bands = " + bands.Count + "\r\n" +
                "header offset = 0\r\n" +
                "file type = ENVI Standard\r\n" +
                "data type = 1\r\n" +
                "interleave = bsq\r\n" +
                "byte order = 0\r\n" +
                "band names = {\r\n " + names + "\r\n}\r\n";
            File.WriteAllText(hdrPath, hdr, System.Text.Encoding.ASCII);
        }

        // ============================== ÉLÉMENT STRUCTURANT DISQUE ==============================
        // Décalages (dx, dy) des pixels couverts par un disque de rayon r centré sur (0,0).
        // r=1 -> 5 pixels, r=2 -> 13 pixels, r=3 -> 29 pixels.
        private List<SDPoint> GetDiskOffsets(int r)
        {
            var offs = new List<SDPoint>();
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                    if (dx * dx + dy * dy <= r * r) offs.Add(new SDPoint(dx, dy));
            return offs;
        }

        // Vrai si le disque centré en (x,y) déborde de l'image : pixel de bord, non traité
        // (il conserve sa valeur précédente)
        private bool IsBorder(int x, int y, int W, int H, List<SDPoint> offs)
        {
            foreach (var p in offs)
            {
                int nx = x + p.X, ny = y + p.Y;
                if (nx < 0 || ny < 0 || nx >= W || ny >= H) return true;
            }
            return false;
        }

        // ============================== SYSTÈME DE NUMÉRATION ==============================
        // Compare Num(A) et Num(B), où Num(V) = v1*X^(m-1) + ... + vm, sans jamais calculer ces
        // nombres (qui dépassent la précision d'un double au-delà de 6 bandes).
        // Comparaison exacte : comme chaque chiffre est < X, le premier chiffre (bande 1, 2, ...)
        // qui diffère décide du signe de Num(A) - Num(B), car la somme de tous les chiffres
        // suivants vaut au plus X^(m-j) - 1 < X^(m-j).
        // Retourne -1 si Num(A) < Num(B), 0 si égalité (vecteurs identiques), 1 si Num(A) > Num(B).
        private int CompareNum(List<int[,]> imgA, int xa, int ya, List<int[,]> imgB, int xb, int yb)
        {
            for (int k = 0; k < imgA.Count; k++)
            {
                int va = imgA[k][xa, ya];
                int vb = imgB[k][xb, yb];
                if (va < vb) return -1;
                if (va > vb) return 1;
            }
            return 0;
        }

        // Infimum (Num minimal, calculé sur imagesErodPrec) et supremum (Num maximal, calculé sur
        // imagesDilatePrec) des pixels-vecteurs du disque de rayon 'rayonDisque' centré en (x,y).
        // Num étant injective, les deux extrema sont uniques (à égalité de Num, les vecteurs sont
        // identiques : on garde le premier rencontré).
        private void MinMaxVecteurs(List<int[,]> imagesErodPrec, List<int[,]> imagesDilatePrec, int x, int y, ref int sMin, ref int tMin, ref int sMax, ref int tMax, int rayonDisque)
        {
            var offs = GetDiskOffsets(rayonDisque);
            sMin = x + offs[0].X; tMin = y + offs[0].Y;
            sMax = x + offs[0].X; tMax = y + offs[0].Y;
            for (int z = 1; z < offs.Count; z++)
            {
                int cx = x + offs[z].X, cy = y + offs[z].Y;
                // Érosion : on garde le vecteur de plus petite valeur Num
                if (CompareNum(imagesErodPrec, cx, cy, imagesErodPrec, sMin, tMin) < 0) { sMin = cx; tMin = cy; }
                // Dilatation : on garde le vecteur de plus grande valeur Num
                if (CompareNum(imagesDilatePrec, cx, cy, imagesDilatePrec, sMax, tMax) > 0) { sMax = cx; tMax = cy; }
            }
        }

        // ============================== RECONSTRUCTION MORPHOLOGIQUE ==============================
        // Ouverture par reconstruction : R^delta_f( epsilon_Bi(f) )  (marqueur = érodé, masque = f)
        // Fermeture par reconstruction : R^epsilon_f( delta_Bi(f) )  (marqueur = dilaté, masque = f)
        // On itère les dilatations/érosions géodésiques d'ordre 1 jusqu'à stabilité, ou jusqu'à
        // atteindre le nombre d'itérations maximal fixé (0 = illimité), ou jusqu'au pourcentage
        // de ressemblance demandé.
        private void Reconstruction(List<int[,]> imagesMat, List<int[,]> imagesErodeInit, List<int[,]> imagesDilateInit, int itStabilite, double prStabilite, ref List<int[,]> gNewOuvert, ref List<int[,]> gNewFerme)
        {
            List<int[,]> gLastFerme = new List<int[,]>();
            List<int[,]> gLastOuvert = new List<int[,]>();
            for (int k = 0; k < imagesMat.Count; k++)
            {
                gNewFerme.Add(new int[imagesMat[0].GetLength(0), imagesMat[0].GetLength(1)]);
                gNewOuvert.Add(new int[imagesMat[0].GetLength(0), imagesMat[0].GetLength(1)]);
                // Marqueurs initiaux : image érodée (ouverture) et image dilatée (fermeture)
                gLastOuvert.Add((int[,])imagesErodeInit[k].Clone());
                gLastFerme.Add((int[,])imagesDilateInit[k].Clone());
            }
            int toleranceStabilite = 0;
            if (itStabilite == 0) itStabilite = int.MaxValue; // 0 = nombre d'itérations non limité
            bool stopErod = false, stopDilat = false;
            while (((!stopDilat) || (!stopErod)) && (toleranceStabilite < itStabilite))
            {
                stopErod = true; stopDilat = true;
                // Une étape de dilatation / érosion géodésique d'ordre 1
                ErosionDilatationGeodesique(imagesMat, gLastFerme, gLastOuvert, prStabilite, ref gNewFerme, ref gNewOuvert, ref stopErod, ref stopDilat);
                // Les images calculées deviennent les marqueurs de l'itération suivante
                for (int k = 0; k < imagesMat.Count; k++)
                {
                    gLastOuvert[k] = (int[,])gNewOuvert[k].Clone();
                    gLastFerme[k] = (int[,])gNewFerme[k].Clone();
                }
                toleranceStabilite++;
            }
        }

        // Une étape géodésique d'ordre 1 avec B1 (disque de rayon 1) :
        //   dilatation géodésique : delta_f^(1)(h)   = min_Num( delta_B1(h),   f )  -> ouverture
        //   érosion géodésique    : epsilon_f^(1)(h) = max_Num( epsilon_B1(h), f )  -> fermeture
        private void ErosionDilatationGeodesique(List<int[,]> imagesMat, List<int[,]> gLastFerme, List<int[,]> gLastOuvert, double prStabilite, ref List<int[,]> gNewFerme, ref List<int[,]> gNewOuvert, ref bool stopErod, ref bool stopDilat)
        {
            int W = imagesMat[0].GetLength(0), H = imagesMat[0].GetLength(1);
            var offsB1 = GetDiskOffsets(1);
            int ressemblanceOuverture = 0, ressemblanceFermeture = 0;
            for (int x = 0; x < W; x++)
            {
                for (int y = 0; y < H; y++)
                {
                    if (IsBorder(x, y, W, H, offsB1))
                    {
                        // Pixel de bord : valeur précédente du marqueur conservée
                        for (int k = 0; k < imagesMat.Count; k++)
                        {
                            gNewFerme[k][x, y] = gLastFerme[k][x, y];
                            gNewOuvert[k][x, y] = gLastOuvert[k][x, y];
                        }
                    }
                    else
                    {
                        // Infimum (érosion) du marqueur de fermeture et supremum (dilatation) du marqueur d'ouverture dans B1
                        int sMax = 0, tMax = 0, sMin = 0, tMin = 0;
                        MinMaxVecteurs(gLastFerme, gLastOuvert, x, y, ref sMin, ref tMin, ref sMax, ref tMax, 1);

                        // Érosion géodésique : max_Num( epsilon_B1(h), f )
                        if (CompareNum(imagesMat, x, y, gLastFerme, sMin, tMin) < 0) // Num(f) < Num(epsilon_B1(h))
                            for (int k = 0; k < imagesMat.Count; k++) gNewFerme[k][x, y] = gLastFerme[k][sMin, tMin];
                        else
                            for (int k = 0; k < imagesMat.Count; k++) gNewFerme[k][x, y] = imagesMat[k][x, y];

                        // Dilatation géodésique : min_Num( delta_B1(h), f )
                        if (CompareNum(gLastOuvert, sMax, tMax, imagesMat, x, y) < 0) // Num(delta_B1(h)) < Num(f)
                            for (int k = 0; k < imagesMat.Count; k++) gNewOuvert[k][x, y] = gLastOuvert[k][sMax, tMax];
                        else
                            for (int k = 0; k < imagesMat.Count; k++) gNewOuvert[k][x, y] = imagesMat[k][x, y];

                        // Test de stabilité : le pixel est-il identique à l'itération précédente (toutes bandes) ?
                        int locOuv = 0, locFerm = 0;
                        for (int k = 0; k < imagesMat.Count; k++)
                        {
                            if (gNewFerme[k][x, y] != gLastFerme[k][x, y]) stopErod = false; else locFerm++;
                            if (gNewOuvert[k][x, y] != gLastOuvert[k][x, y]) stopDilat = false; else locOuv++;
                        }
                        if (locOuv == imagesMat.Count) ressemblanceOuverture++;
                        if (locFerm == imagesMat.Count) ressemblanceFermeture++;
                    }
                }
            }
            // Stabilité par pourcentage de ressemblance entre deux images successives
            double tauxOuv = (double)ressemblanceOuverture / (W * H);
            double tauxFerm = (double)ressemblanceFermeture / (W * H);
            if (tauxOuv >= prStabilite) stopDilat = true;
            if (tauxFerm >= prStabilite) stopErod = true;
        }

        // ============================== OUVERTURE / FERMETURE STANDARD ==============================
        // Ouverture = dilatation de l'érodé ; fermeture = érosion du dilaté (même ES de taille 'rayon'),
        // obtenues par 'rayon' itérations de l'ES de base B1.
        private void OuvertureFermetureStandard(List<int[,]> imagesErodeInit, List<int[,]> imagesDilateInit, ref List<int[,]> imagesOuvertesStandards, ref List<int[,]> imagesFermeesStandards, int rayon)
        {
            List<int[,]> imagesErodPrec = new List<int[,]>();
            List<int[,]> imagesDilatePrec = new List<int[,]>();
            for (int k = 0; k < imagesErodeInit.Count; k++)
            {
                imagesOuvertesStandards.Add(new int[imagesErodeInit[0].GetLength(0), imagesErodeInit[0].GetLength(1)]);
                imagesFermeesStandards.Add(new int[imagesErodeInit[0].GetLength(0), imagesErodeInit[0].GetLength(1)]);
                // Fermeture : on érode l'image dilatée ; ouverture : on dilate l'image érodée
                imagesErodPrec.Add((int[,])imagesDilateInit[k].Clone());
                imagesDilatePrec.Add((int[,])imagesErodeInit[k].Clone());
            }
            var offsB1 = GetDiskOffsets(1);
            int W = imagesErodeInit[0].GetLength(0), H = imagesErodeInit[0].GetLength(1);
            for (int elemStruct = 0; elemStruct < rayon; elemStruct++)
            {
                for (int x = 0; x < W; x++)
                    for (int y = 0; y < H; y++)
                    {
                        if (IsBorder(x, y, W, H, offsB1))
                        {
                            // Pixel de bord : valeur précédente conservée
                            for (int k = 0; k < imagesErodeInit.Count; k++)
                            {
                                imagesOuvertesStandards[k][x, y] = imagesDilatePrec[k][x, y];
                                imagesFermeesStandards[k][x, y] = imagesErodPrec[k][x, y];
                            }
                        }
                        else
                        {
                            int sMax = 0, tMax = 0, sMin = 0, tMin = 0;
                            MinMaxVecteurs(imagesErodPrec, imagesDilatePrec, x, y, ref sMin, ref tMin, ref sMax, ref tMax, 1);
                            for (int k = 0; k < imagesErodeInit.Count; k++)
                            {
                                imagesFermeesStandards[k][x, y] = imagesErodPrec[k][sMin, tMin];
                                imagesOuvertesStandards[k][x, y] = imagesDilatePrec[k][sMax, tMax];
                            }
                        }
                    }
                for (int k = 0; k < imagesErodeInit.Count; k++)
                {
                    imagesErodPrec[k] = (int[,])imagesFermeesStandards[k].Clone();
                    imagesDilatePrec[k] = (int[,])imagesOuvertesStandards[k].Clone();
                }
            }
        }

        // ============================== ÉROSION / DILATATION ==============================
        // Érosion (Num minimal) et dilatation (Num maximal) multivaluées de taille 'rayon', obtenues
        // par 'rayon' itérations de l'ES de base B1 (ε_Bλ = ε_B1^λ, δ_Bλ = δ_B1^λ).
        private void ErosionDilatationInit(List<int[,]> imagesMat, ref List<int[,]> imagesErodeInit, ref List<int[,]> imagesDilateInit, int rayon)
        {
            List<int[,]> imagesErodPrec = new List<int[,]>();
            List<int[,]> imagesDilatePrec = new List<int[,]>();
            for (int k = 0; k < imagesMat.Count; k++)
            {
                imagesDilateInit.Add(new int[imagesMat[0].GetLength(0), imagesMat[0].GetLength(1)]);
                imagesErodeInit.Add(new int[imagesMat[0].GetLength(0), imagesMat[0].GetLength(1)]);
                imagesErodPrec.Add((int[,])imagesMat[k].Clone());
                imagesDilatePrec.Add((int[,])imagesMat[k].Clone());
            }
            var offsB1 = GetDiskOffsets(1);
            int W = imagesMat[0].GetLength(0), H = imagesMat[0].GetLength(1);
            for (int elemStruct = 0; elemStruct < rayon; elemStruct++)
            {
                for (int x = 0; x < W; x++)
                    for (int y = 0; y < H; y++)
                    {
                        if (IsBorder(x, y, W, H, offsB1))
                        {
                            // Pixel de bord : valeur précédente conservée
                            for (int k = 0; k < imagesMat.Count; k++)
                            {
                                imagesDilateInit[k][x, y] = imagesDilatePrec[k][x, y];
                                imagesErodeInit[k][x, y] = imagesErodPrec[k][x, y];
                            }
                        }
                        else
                        {
                            int sMax = 0, tMax = 0, sMin = 0, tMin = 0;
                            MinMaxVecteurs(imagesErodPrec, imagesDilatePrec, x, y, ref sMin, ref tMin, ref sMax, ref tMax, 1);
                            for (int k = 0; k < imagesMat.Count; k++)
                            {
                                imagesErodeInit[k][x, y] = imagesErodPrec[k][sMin, tMin];
                                imagesDilateInit[k][x, y] = imagesDilatePrec[k][sMax, tMax];
                            }
                        }
                    }
                for (int k = 0; k < imagesMat.Count; k++)
                {
                    imagesErodPrec[k] = (int[,])imagesErodeInit[k].Clone();
                    imagesDilatePrec[k] = (int[,])imagesDilateInit[k].Clone();
                }
            }
        }

        // ============================== CONVERSION BITMAP -> MATRICES ==============================
        // Une matrice int[x, y] par bande (valeur du canal rouge = niveau de gris)
        private List<int[,]> bmpToMat(List<Bitmap> imagesBmp)
        {
            List<int[,]> imagesMat = new List<int[,]>();
            for (int z = 0; z < imagesBmp.Count; z++)
            {
                imagesMat.Add(new int[imagesBmp[z].Width, imagesBmp[z].Height]);
                for (int x = 0; x < imagesBmp[z].Width; x++)
                    for (int y = 0; y < imagesBmp[z].Height; y++)
                        imagesMat[z][x, y] = imagesBmp[z].GetPixel(x, y).R;
            }
            return imagesMat;
        }
    }
}
