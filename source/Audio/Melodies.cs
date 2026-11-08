namespace ZonderqOS.Audio
{
    public sealed class Track
    {
        public string Title { get; }
        public string Author { get; }
        public Note[] Notes { get; }

        public Track(string title, string author, Note[] notes)
        {
            Title = title ?? "Bez tytulu";
            Author = author ?? "Nieznany";
            Notes = notes ?? System.Array.Empty<Note>();
        }
    }

    public static class Melodies
    {
        // Standard Note Frequencies in Hz
        public const int C4 = 262, D4 = 294, E4 = 330, F4 = 349, G4 = 392, A4 = 440, B4 = 494;
        public const int C5 = 523, D5 = 587, E5 = 659, F5 = 698, G5 = 784, A5 = 880, B5 = 988;
        public const int C6 = 1046, D6 = 1175, E6 = 1318, F6 = 1397, G6 = 1568, A6 = 1760, B6 = 1976;
        public const int Fs4 = 370, Gs4 = 415, As4 = 466, Cs5 = 554, Ds5 = 622, Fs5 = 740, Gs5 = 831, As5 = 932;
        public const int Cs6 = 1109, Ds6 = 1245, Fs6 = 1480, Gs6 = 1661;
        public const int REST = 0;

        public static readonly Track[] Playlist = new Track[]
        {
            new Track("Super Mario Bros", "Koji Kondo", new Note[]
            {
                new Note(E5, 120), new Note(E5, 120), new Note(REST, 120), new Note(E5, 120),
                new Note(REST, 120), new Note(C5, 120), new Note(E5, 120), new Note(REST, 120),
                new Note(G5, 240), new Note(REST, 240), new Note(G4, 240), new Note(REST, 240),
                new Note(C5, 200), new Note(REST, 100), new Note(G4, 200), new Note(REST, 100),
                new Note(E4, 200), new Note(REST, 100), new Note(A4, 180), new Note(B4, 180),
                new Note(As4, 140), new Note(A4, 180), new Note(G4, 160), new Note(E5, 160),
                new Note(G5, 160), new Note(A5, 200), new Note(F5, 140), new Note(G5, 140),
                new Note(REST, 80), new Note(E5, 160), new Note(C5, 140), new Note(D5, 140),
                new Note(B4, 220), new Note(REST, 200)
            }),

            new Track("Tetris (Korobeiniki)", "Hirokazu Tanaka", new Note[]
            {
                new Note(E5, 250), new Note(B4, 125), new Note(C5, 125), new Note(D5, 250),
                new Note(C5, 125), new Note(B4, 125), new Note(A4, 250), new Note(A4, 125),
                new Note(C5, 125), new Note(E5, 250), new Note(D5, 125), new Note(C5, 125),
                new Note(B4, 375), new Note(C5, 125), new Note(D5, 250), new Note(E5, 250),
                new Note(C5, 250), new Note(A4, 250), new Note(A4, 350), new Note(REST, 125),
                new Note(D5, 250), new Note(F5, 125), new Note(A5, 250), new Note(G5, 125),
                new Note(F5, 125), new Note(E5, 375), new Note(C5, 125), new Note(E5, 250),
                new Note(D5, 125), new Note(C5, 125), new Note(B4, 250), new Note(B4, 125),
                new Note(C5, 125), new Note(D5, 250), new Note(E5, 250), new Note(C5, 250),
                new Note(A4, 250), new Note(A4, 350), new Note(REST, 200)
            }),

            new Track("Star Wars (Imperial March)", "John Williams", new Note[]
            {
                new Note(A4, 350), new Note(A4, 350), new Note(A4, 350), new Note(F4, 250),
                new Note(C5, 125), new Note(A4, 350), new Note(F4, 250), new Note(C5, 125),
                new Note(A4, 500), new Note(REST, 150),
                new Note(E5, 350), new Note(E5, 350), new Note(E5, 350), new Note(F5, 250),
                new Note(C5, 125), new Note(Gs4, 350), new Note(F4, 250), new Note(C5, 125),
                new Note(A4, 500), new Note(REST, 200)
            }),

            new Track("Nokia Tune (Grande Valse)", "Francisco Tarrega", new Note[]
            {
                new Note(E6, 140), new Note(D6, 140), new Note(Fs5, 250), new Note(Gs5, 250),
                new Note(Cs6, 140), new Note(B5, 140), new Note(D5, 250), new Note(E5, 250),
                new Note(B5, 140), new Note(A5, 140), new Note(Cs5, 250), new Note(E5, 250),
                new Note(A5, 500), new Note(REST, 200)
            }),

            new Track("Pac-Man Theme", "Toshio Kai", new Note[]
            {
                new Note(B4, 130), new Note(B5, 130), new Note(Fs5, 130), new Note(Ds5, 130),
                new Note(B5, 100), new Note(Fs5, 150), new Note(Ds5, 200),
                new Note(C5, 130), new Note(C6, 130), new Note(G5, 130), new Note(E5, 130),
                new Note(C6, 100), new Note(G5, 150), new Note(E5, 200),
                new Note(B4, 130), new Note(B5, 130), new Note(Fs5, 130), new Note(Ds5, 130),
                new Note(B5, 100), new Note(Fs5, 150), new Note(Ds5, 200),
                new Note(Ds5, 100), new Note(E5, 100), new Note(F5, 100), new Note(Fs5, 100),
                new Note(G5, 100), new Note(Gs5, 100), new Note(A5, 100), new Note(B5, 200)
            }),

            new Track("ZOnderqOS Startup Chime", "ZOnderq", new Note[]
            {
                new Note(C5, 160), new Note(E5, 160), new Note(G5, 160), new Note(C6, 380)
            })
        };
    }
}
