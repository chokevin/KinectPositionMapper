# Creating Python Script to scrape text from text output from textfile outputted from file

class KinectPosition(object):
    def __init__(self, Id, x, y, z):
        self.Id = Id;
        self.x = x;
        self.y = y;
        self.z = z;

def read_from_kinect_file():
    f = open("/mnt/c/Users/AmbientUH/AppData/Local/Packages/159eb80c-857e-4bc4-a0ba-9d5266c417ff_zkzqb6kcb8758/LocalState/debugwrite.txt", "r")
    coordList = []
    for line in f:
        coords = line.split(" ");
        coordList.append(KinectPosition(None, coords[0], coords[1], coords[2]))
    f.close()
    return coordList

def print_coord_list(coordList):
    for coord in coordList:
        print("X:" + coord.x + " Y:" + coord.y + " Z:" + coord.z)

print_coord_list(read_from_kinect_file())
